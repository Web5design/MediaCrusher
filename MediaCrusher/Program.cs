using System;
using System.IO;
using Newtonsoft.Json;
using RedditSharp;

namespace MediaCrusher
{
    class Program
    {
        public static int Main(string[] args)
        {
            var config = new Configuration();
            if (File.Exists("config.json"))
                config = JsonConvert.DeserializeObject<Configuration>(File.ReadAllText("config.json"));
            else
            {
                File.WriteAllText("config.json", JsonConvert.SerializeObject(config));
                Console.WriteLine("Saved empty configuration in config.json, populate it and restart.");
                return 1;
            }
            var reddit = new Reddit();
            reddit.LogIn(config.Username, config.Password);
            return 0;
        }
    }
}
