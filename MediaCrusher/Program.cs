using System;
using System.IO;
using Newtonsoft.Json;
using RedditSharp;
using System.Threading;
using System.Linq;
using System.Threading.Tasks;
using System.Net;

namespace MediaCrusher
{
    static class Program
    {
        readonly static string[] SupportedContentTypes = new[] { "image/jpg", "image/jpeg", "image/png", "image/svg", "image/gif", "video/mp4", "video/ogv", "audio/mp3" };

        public static Reddit Reddit { get; set; }
        public static Timer Timer { get; set; }

        public static int Main(string[] args)
        {
            var config = new Configuration();
            if (File.Exists("config.json"))
                config = JsonConvert.DeserializeObject<Configuration>(File.ReadAllText("config.json"));
            else
            {
                File.WriteAllText("config.json", JsonConvert.SerializeObject(config, Formatting.Indented));
                Console.WriteLine("Saved empty configuration in config.json, populate it and restart.");
                return 1;
            }
            Reddit = new Reddit();
            Reddit.LogIn(config.Username, config.Password);

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
                        continue;
                    var post = Reddit.GetThingByFullname(comment.ParentId) as Post;
                    if (post.Domain == "mediacru.sh")
                        comment.Reply("This post is already on mediacru.sh, silly!");
                    else
                    {
                        var task = Task.Factory.StartNew(() =>
                        {
                            var request = (HttpWebRequest)WebRequest.Create(post.Url);
                            request.Method = "HEAD";
                            var response = request.GetResponse() as HttpWebResponse;
                            if (!SupportedContentTypes.Any(c => c == response.ContentType))
                            {
                                comment.Reply("This post isn't a supported media type. :(");
                                return;
                            }
                            else if (response.StatusCode != HttpStatusCode.OK)
                            {
                                comment.Reply("There was an error fetching this file. :(");
                                return;
                            }
                            else
                            {
                                // Let's do this
                                var client = new WebClient();
                                var file = client.DownloadData(post.Url);
                                request = (HttpWebRequest)WebRequest.Create("https://mediacru.sh/upload/");
                                request.Method = "POST";
                                var builder = new MultipartFormBuilder(request);
                                builder.AddFile("file", "foobar" + Path.GetExtension(new Uri(post.Url).LocalPath), file, response.ContentType);
                                builder.Finish();
                                try
                                {
                                    response = request.GetResponse() as HttpWebResponse;
                                }
                                catch (WebException e)
                                {
                                    comment.Reply("MediaCrush didn't like this for some reason. Sorry :(");
                                    return;
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
                                    if (text == "done")
                                    {
                                        comment.Reply("Done! https://mediacru.sh/" + hash);
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
    }
}
