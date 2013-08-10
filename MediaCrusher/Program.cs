using System.Collections.Generic;
using System;
using System.IO;
using Newtonsoft.Json;
using RedditSharp;
using System.Threading;
using System.Linq;
using System.Threading.Tasks;
using System.Net;
using Newtonsoft.Json.Linq;

namespace MediaCrusher
{
    static class Program
    {
        readonly static string[] SupportedContentTypes = new[] { "image/jpg", "image/jpeg", "image/png", "image/svg", "image/gif", "video/mp4", "video/ogv", "audio/mp3" };

        public static Configuration Config { get; set; }
        public static Reddit Reddit { get; set; }
        public static Timer Timer { get; set; }

        public static int Main(string[] args)
        {
            Config = new Configuration();
            if (File.Exists("config.json"))
                Config = JsonConvert.DeserializeObject<Configuration>(File.ReadAllText("config.json"));
            else
            {
                File.WriteAllText("config.json", JsonConvert.SerializeObject(Config, Formatting.Indented));
                Console.WriteLine("Saved empty configuration in config.json, populate it and restart.");
                return 1;
            }
            Reddit = new Reddit();
            Reddit.LogIn(Config.Username, Config.Password);

            Timer = new Timer(o => DoUpdate(), null, 0, 30000);

            Console.WriteLine("Press 'q' to exit.");
            ConsoleKeyInfo cki;
            do cki = Console.ReadKey(true);
            while (cki.KeyChar != 'q');

            return 0;
        }

        public static void DoUpdate()
        {
            Console.WriteLine("Running update...");
            try
            {
                var messages = Reddit.User.GetUnreadMessages();
                foreach (var message in messages)
                {
                    var comment = message as Comment;
                    if (comment == null)
                    {
                        if (message is PrivateMessage)
                            (message as PrivateMessage).SetAsRead();
                        continue;
                    }
                    comment.SetAsRead();
                    if (!comment.Body.Contains("/u/MediaCrusher"))
                        continue;
                    Console.WriteLine("Handling {0}", comment.FullName);
                    var post = Reddit.GetThingByFullname("t3_" + comment.LinkId) as Post;
                    if (post.Domain == "mediacru.sh")
                        comment.Reply("This post is already on mediacru.sh, silly!");
                    else
                    {
                        var uri = new Uri(post.Url);
                        if (uri.Host == "imgur.com" && !uri.LocalPath.StartsWith("/a/"))
                        {
                            // Swap out for i.imgur.com
                            uri = new Uri("http://i.imgur.com/" + uri.LocalPath + ".png"); // Note: extension doesn't need to be correct
                        }
                        var task = Task.Factory.StartNew(() =>
                        {
                            var request = (HttpWebRequest)WebRequest.Create(uri);
                            request.Method = "HEAD";
                            var response = request.GetResponse() as HttpWebResponse;
                            if (!SupportedContentTypes.Any(c => c == response.ContentType))
                            {
                                comment.Reply("This post isn't a supported media type. :(");
                                response.Close();
                                return;
                            }
                            else if (response.StatusCode != HttpStatusCode.OK)
                            {
                                comment.Reply("There was an error fetching this file. :(");
                                response.Close();
                                return;
                            }
                            else
                            {
                                response.Close();
                                // Let's do this
                                var client = new WebClient();
                                var file = client.DownloadData(uri);
                                request = (HttpWebRequest)WebRequest.Create("https://mediacru.sh/upload/");
                                request.Method = "POST";
                                var builder = new MultipartFormBuilder(request);
                                var ext = response.ContentType.Split('/')[1];
                                builder.AddFile("file", "foobar." + ext, file, response.ContentType);
                                builder.Finish();
                                try
                                {
                                    response = request.GetResponse() as HttpWebResponse;
                                }
                                catch (WebException e)
                                {
                                    try
                                    {
                                        response = e.Response as HttpWebResponse;
                                        if (response.StatusCode != HttpStatusCode.Conflict)
                                        {
                                            comment.Reply("MediaCrush didn't like this for some reason. Sorry :(");
                                            return;
                                        }
                                    }
                                    catch
                                    {
                                        comment.Reply("MediaCrush didn't like this for some reason. Sorry :(");
                                        return;
                                    }
                                }
                                string hash;
                                using (var stream = new StreamReader(response.GetResponseStream()))
                                    hash = stream.ReadToEnd();
                                while (true)
                                {
                                    request = (HttpWebRequest)WebRequest.Create("https://mediacru.sh/upload/status/" + hash);
                                    request.Method = "GET";
                                    response = request.GetResponse() as HttpWebResponse;
                                    string text;
                                    using (var stream = new StreamReader(response.GetResponseStream()))
                                        text = stream.ReadToEnd();
                                    response.Close();
                                    try
                                    {
                                        if (text == "done")
                                        {
                                            request = (HttpWebRequest)WebRequest.Create("https://mediacru.sh/" + hash + ".json");
                                            request.Method = "GET";
                                            response = request.GetResponse() as HttpWebResponse;
                                            var json = JObject.Parse(new StreamReader(response.GetResponseStream()).ReadToEnd());
                                            response.Close();
                                            var compliment = GetCompliment();
                                            var compression = (int)(json["compression"].Value<double>() * 100);
                                            if (compression >= 100)
                                            {
                                                comment.Reply(string.Format("Done! It loads **{0}% faster** now. https://mediacru.sh/{1}\n\n*{2}* " +
                                                    "^^[faq](http://www.reddit.com/r/MediaCrush/wiki/mediacrusher) " +
                                                    "^^- ^^[upload](https://mediacru.sh)", compression, hash, compliment));
                                            }
                                            else
                                            {
                                                comment.Reply(string.Format("Done! https://mediacru.sh/{0}\n\n*{1}* " +
                                                    "^^[faq](http://www.reddit.com/r/MediaCrush/wiki/mediacrusher) " +
                                                    "^^- ^^[upload](https://mediacru.sh)", hash, compliment));
                                            }
                                            Console.WriteLine("https://mediacru.sh/" + hash);
                                            return;
                                        }
                                        else if (text == "timeout")
                                        {
                                            comment.Reply("This took too long for us to process :(");
                                            return;
                                        }
                                        else if (text == "error")
                                        {
                                            comment.Reply("Something went wrong :(");
                                            return;
                                        }
                                    }
                                    catch (RateLimitException e)
                                    {
                                        Console.WriteLine("Rate limited - waiting {0} minutes", e.TimeToReset.TotalMinutes);
                                        Thread.Sleep(e.TimeToReset);
                                    }
                                }
                            }
                        });
                    }
                }
                Console.WriteLine("Done.");
            }
            catch (Exception e)
            {
                Console.WriteLine("Exception occured during update:");
                Console.WriteLine(e.ToString());
            }
            Timer.Change(30000, Timeout.Infinite);
        }

        static Random Random = new Random();
        public static string GetCompliment()
        {
            return Config.Compliments[Random.Next(Config.Compliments.Length)];
        }
    }
}
