using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Xml;
using HtmlAgilityPack;
using Microsoft.AspNetCore.Mvc;
using Microsoft.SyndicationFeed;
using Microsoft.SyndicationFeed.Rss;
using System.Threading;
using System.Net;

namespace rsscrawlercore.Controllers
{
    public class GZipWebClient : WebClient
    {
        protected override WebRequest GetWebRequest(Uri address)
        {
            HttpWebRequest request = (HttpWebRequest)base.GetWebRequest(address);
            request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            return request;
        }
    }
    public class GamerskyEntry
    {
        public string Link { get; set; }
        public string Title { get; set; }
        public DateTime PubDate { get; set; }
        public string Content { get; set; }
    }
    public class WotShopEntry
    {
        public string WGId {get;set;}
        public string Title { get; set; }
        public string Link { get; set; }
        public DateTime PubDate { get; set; }
        public string Content { get; set; }
    }
    public class RssController : Controller
    {
        public static string DateTimeFormat = @"yyyy-MM-dd HH:mm:ss";
        public static DateTime LastUpdateTime = DateTime.UtcNow;
        public static ReaderWriterLockSlim FileLock = new ReaderWriterLockSlim();
        public HtmlDocument DownloadGzipHtml(string siteUrl)
        {
            using (var wc = new GZipWebClient())
            {
                wc.Headers[HttpRequestHeader.AcceptEncoding] = "gzip";
                wc.Headers[HttpRequestHeader.UserAgent] = @"Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:62.0) Gecko/20100101 Firefox/62.0";
                wc.Encoding = Encoding.UTF8;
                string html = wc.DownloadString(siteUrl);
                var htmldocObject = new HtmlDocument();
                htmldocObject.LoadHtml(html);
                return htmldocObject;
            }
        }
        public string DownloadGzipString(string siteUrl)
        {
            using (var wc = new GZipWebClient())
            {
                wc.Headers[HttpRequestHeader.AcceptEncoding] = "gzip";
                wc.Headers[HttpRequestHeader.UserAgent] = @"Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:62.0) Gecko/20100101 Firefox/62.0";
                wc.Headers[HttpRequestHeader.Referer] = @"https://shop.wot.360.cn/main";
                wc.Encoding = Encoding.UTF8;
                string payload = wc.DownloadString(siteUrl);
                return payload;
            }
        }
        #region Gamersky feed
        private async Task<string> FormatGamerskyFeed(IEnumerable<GamerskyEntry> entries)
        {
            var sw = new StringWriterWithEncoding(Encoding.UTF8);

            using (XmlWriter xmlWriter = XmlWriter.Create(sw, new XmlWriterSettings() { Async = true, Indent = true }))
            {
                var writer = new RssFeedWriter(xmlWriter);
                await writer.WriteTitle("Gamersky RSS feed");
                await writer.WriteDescription("Gamersky RSS feed");
                await writer.Write(new SyndicationLink(new Uri("https://www.gamersky.com/")));
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
        public async Task<IActionResult> GetGamerskyCached()
        {
            await UpdateGamersky();
            var cacheFile = Path.Combine(Path.GetTempPath(), "gamerskyrss.xml");
            FileLock.EnterReadLock();
            string feed = System.IO.File.ReadAllText(cacheFile);
            FileLock.ExitReadLock();
            return Content(feed, "application/xml");
        }

        public async Task UpdateGamersky()
        {
            LastUpdateTime = DateTime.Now;
            var GamerskyURL = @"https://www.gamersky.com";

            //var web = new HtmlWeb();
            Console.WriteLine($"Fetching: {GamerskyURL}");
            HtmlDocument doc = DownloadGzipHtml(GamerskyURL);
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
                    entry.PubDate = LastUpdateTime;
                    entries.Add(entry);
                }
            }
            //var tasks = entries.Select(async e =>
            Parallel.ForEach(entries, new ParallelOptions { MaxDegreeOfParallelism = 32 }, e =>
             {
                 try
                 {
                     //Console.WriteLine($"Fetching: {e.Title} {e.Link}");
                     //var entryweb = new HtmlWeb();
                     var entrydoc = DownloadGzipHtml(e.Link);
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

            FileLock.EnterWriteLock();
            var cacheFile = Path.Combine(Path.GetTempPath(), "gamerskyrss.xml");
            if (System.IO.File.Exists(cacheFile))
                System.IO.File.Delete(cacheFile);
            System.IO.File.WriteAllText(cacheFile, feed);
            FileLock.ExitWriteLock();

            Console.WriteLine($"{DateTime.Now.ToString(DateTimeFormat)}:Cache Update Done.");
        }
        #endregion

        #region WoT Shop feed
        [HttpGet, Route("/rss/wotshop360")]
        public async Task<IActionResult> GetWoTShop360Async()
        {
            string WoTShopURL = @"https://shop.wot.360.cn/api/product/list?game_id=1&type=main";
            string RawJson = DownloadGzipString(WoTShopURL);
            JsonDocument shopResponse = JsonDocument.Parse(RawJson);
            if(shopResponse.RootElement.GetProperty("errno").GetInt32() == 0)
            {
                List<WotShopEntry> Entries = new List<WotShopEntry>();
                Console.WriteLine("Response no error, continue parsing...");
                var list = shopResponse.RootElement.GetProperty("data").GetProperty("list").EnumerateArray().ToList();
                foreach( var item in list)
                {
                    try
                    {
                        WotShopEntry e = new WotShopEntry();
                        e.Title = item.GetProperty("name").GetString();
                        e.Link = @"https://shop.wot.360.cn/detail.html?goods_id="+ item.GetProperty("product_code").GetString();
                        var price = item.GetProperty("price").GetString();
                        var picURL= item.GetProperty("pic").GetString();
                        e.PubDate = DateTime.Parse(item.GetProperty("cache_time").ToString());
                        e.Content = $"<img src={picURL}><br><p>{price} RMB</p><br>" + item.GetProperty("descript").GetString() + "<br><p>";
                        foreach ( var i in item.GetProperty("package_content").EnumerateArray().ToList() )
                        {
                            e.Content = e.Content + "<br>" + i.GetProperty("content").GetString();
                        }
                        e.Content+="</p>";
                        Entries.Add(e);
                    }
                    catch(Exception ex)
                    {
                        Console.WriteLine("Error processing item:");
                        Console.WriteLine(item.ToString());
                        Console.WriteLine(ex.ToString());
                    }
                }
                var feed = await FormatWotShopFeed(Entries);
                return Content(feed, "application/xml");
            }
            else
            {
                Console.WriteLine("Response code is not 0:");
                Console.WriteLine(RawJson);
                return StatusCode(503);
            }
            
            
        }

        private async Task<string> FormatWotShopFeed(IEnumerable<WotShopEntry> entries)
        {
            var sw = new StringWriterWithEncoding(Encoding.UTF8);

            using (XmlWriter xmlWriter = XmlWriter.Create(sw, new XmlWriterSettings() { Async = true, Indent = true }))
            {
                var writer = new RssFeedWriter(xmlWriter);
                await writer.WriteTitle("WoTShop360 RSS feed");
                await writer.WriteDescription("WoTShop360 RSS feed");
                await writer.Write(new SyndicationLink(new Uri("https://shop.wot.360.cn/")));
                await writer.WritePubDate(DateTimeOffset.UtcNow);

                foreach (var e in entries)
                {
                    try
                    {
                        var item = new SyndicationItem()
                        {
                            Id = e.WGId,
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

        #endregion
    }
}
