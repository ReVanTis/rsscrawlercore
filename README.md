# rsscrawlercore

Personal asp.net core web crawler for rss content generation.

How to build:

```bash
git clone https://github.com/revantis/rsscrawlercore
dotnet restore
dotnet build
```

Modify host:port in Properties/launchSettings.json, line 24:

```bash
...
      "applicationUrl": "https://localhost:20244",
...
```

and run

```bash
dotnet run
```

or publish then deploy to anywhere you want.

Currently supported feeds:

* [Gamersky](http://www.gamersky.com)

Used library:

* [HtmlAgilityPack](https://www.nuget.org/packages/HtmlAgilityPack/)
* [SyndicationFeed.ReaderWriter](https://github.com/dotnet/SyndicationFeedReaderWriter)
