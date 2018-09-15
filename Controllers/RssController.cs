using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml;
using HtmlAgilityPack;
using Microsoft.AspNetCore.Mvc;
using Microsoft.SyndicationFeed;
using Microsoft.SyndicationFeed.Rss;
namespace rsscrawlercore.Controllers
{
    public class GamerskyEntry
    {
        public string Link { get; set; }
        public string Title { get; set; }
        public DateTime PubDate { get; set; }
        public string Content { get; set; }
    }
    public class RssController : Controller
    {
        public static DateTime LastUpdateTime = DateTime.UtcNow;
        private async Task<string> FormatGamerskyFeed(IEnumerable<GamerskyEntry> entries)
        {
            var sw = new StringWriterWithEncoding(Encoding.UTF8);

            using (XmlWriter xmlWriter = XmlWriter.Create(sw, new XmlWriterSettings() { Async = true, Indent = true }))
            {
                var writer = new RssFeedWriter(xmlWriter);
                await writer.WriteTitle("Gamersky RSS feed");
                await writer.WriteDescription("Gamersky RSS feed");
                await writer.Write(new SyndicationLink(new Uri("http://www.gamersky.com/")));
                await writer.WritePubDate(DateTimeOffset.UtcNow);

                foreach (var e in entries)
                {
                    try
                    {
                        var item = new SyndicationItem()
                        {
                            Id = e.Link,
                            Title = e.Title,
                            Published = e.PubDate,
                            Description = e.Content,
                        };
                        item.AddLink(new SyndicationLink(new Uri(e.Link)));
                        await writer.Write(item);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Issue happens during generating rss for {e.Link}");                        
                        Console.WriteLine(e.Title);
                        Console.WriteLine(e.Content);
                        Console.WriteLine(e.PubDate);
                        
                        Console.WriteLine(ex.ToString());
                    }
                }
                xmlWriter.Flush();
            }
            return sw.ToString();
        }

        [HttpGet, Route("/rss/gamersky")]
        public async Task<IActionResult> GetGamerskyAsync()
        {
            var now = DateTime.Now;
            var cacheFile = Path.Combine(Path.GetTempPath(), "gamerskyrss.xml");
            if (System.IO.File.Exists(cacheFile))
            {
                if (now - LastUpdateTime < new TimeSpan(0, 30, 0))
                {
                    Console.WriteLine("Cache hit!");
                    return Content(System.IO.File.ReadAllText(cacheFile), "application/xml");
                }
                else
                {
                    System.IO.File.Delete(cacheFile);
                }
            }

            LastUpdateTime = now;
            var GamerskyURL = @"https://www.gamersky.com/";
            var web = new HtmlWeb();
            Console.WriteLine($"Fetching: {GamerskyURL}");
            HtmlDocument doc = web.Load(GamerskyURL);
            List<GamerskyEntry> entries = new List<GamerskyEntry>();

            var ptxt = doc.DocumentNode.Descendants().Where(p => p.Name == "ul" && p.HasClass("Ptxt")).ToList();
            foreach (var p in ptxt)
            {
                var lis = p.Descendants().Where(pp => pp.Name == "li" && pp.HasClass("li3")).ToList();
                foreach (var l in lis)
                {
                    var entry = new GamerskyEntry();
                    entry.Link = l.ChildNodes[0].ChildNodes[0].Attributes["href"].Value;
                    if (entry.Link.StartsWith("/"))
                    {
                        entry.Link = "https://www.gamersky.com" + entry.Link;
                    }
                    entry.Title = l.ChildNodes[0].ChildNodes[0].Attributes["title"].Value;
                    entry.PubDate = now;
                    entries.Add(entry);
                }
            }
            //var tasks = entries.Select(async e =>
            Parallel.ForEach(entries, new ParallelOptions { MaxDegreeOfParallelism = 32 }, e =>
             {
                 try
                 {
                     //Console.WriteLine($"Fetching: {e.Title} {e.Link}");
                     var entryweb = new HtmlWeb();
                     var entrydoc = entryweb.Load(e.Link);
                     try
                     {
                         var content = entrydoc.DocumentNode.Descendants().Where(p => p.Name == "div" && p.HasClass("Mid2L_con")).FirstOrDefault();
                         e.Content = content.InnerHtml;
                     }
                     catch (Exception)
                     {
                         Console.WriteLine($"Failed to load content in {e.Link}");
                     }
                     try
                     {
                         var Date = entrydoc.DocumentNode.Descendants().Where(p => p.Name == "div" && p.HasClass("detail")).FirstOrDefault();
                         Regex re = new Regex(@"\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}");
                         var match = re.Match(Date.InnerText);
                         if (match.Success)
                         {
                             e.PubDate = DateTime.Parse(match.Groups[0].ToString());
                         }
                     }
                     catch (Exception)
                     {
                         Console.WriteLine($"Failed to load Datetime in {e.Link}");
                     }
                 }
                 catch (Exception ex)
                 {
                     Console.WriteLine($"Other issue happens during processing {e.Link}");
                     Console.WriteLine(ex.ToString());
                 }
             });

            var feed = await FormatGamerskyFeed(entries);

            System.IO.File.WriteAllText(cacheFile, feed);

            Console.WriteLine("Done.");
            return Content(feed, "application/xml");
        }
    }
}
