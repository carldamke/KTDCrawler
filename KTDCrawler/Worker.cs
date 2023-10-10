using HtmlAgilityPack;

namespace KTDCrawler
{
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;
        private Dictionary<string, (long Size, DateTime LastModified)> downloadedFilesInfo = new Dictionary<string, (long, DateTime)>();

        public Worker(ILogger<Worker> logger)
        {
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("Worker running at: {time}", DateTimeOffset.Now);

                try
                {
                    using (var client = new HttpClient())
                    {
                        string baseUrl = "https://gkv-datenaustausch.de/leistungserbringer/sonstige_leistungserbringer/kostentraegerdateien_sle/";
                        string url = baseUrl + "kostentraegerdateien.jsp";

                        // HttpClient-Anforderung
                        var response = await client.GetAsync(url, stoppingToken);
                        response.EnsureSuccessStatusCode();

                        // HTML-Inhalt abrufen
                        string htmlContent = await response.Content.ReadAsStringAsync();

                        // Html-Analyse mit AgilityPack
                        var htmlDocument = new HtmlDocument();
                        htmlDocument.LoadHtml(htmlContent);

                        // Alle Links mit Endung .ke0 und .ke2
                        var ke0Links = htmlDocument.DocumentNode
                            .Descendants("a")
                            .Where(a => a.Attributes["href"] != null &&
                                        (a.Attributes["href"].Value.EndsWith(".ke0") || a.Attributes["href"].Value.EndsWith(".ke2")))
                            .Select(a => a.Attributes["href"].Value)
                            .ToList();

                        // Existiert Verzeichnis? Wenn nicht, erstellen!
                        Directory.CreateDirectory("DownloadedFiles");

                        // Dateien herunterladen
                        foreach (var link in ke0Links)
                        {
                            // Relative URL->Absolute URL.
                            var absoluteUrl = new Uri(new Uri(baseUrl), link).ToString();

                            var fileName = Path.GetFileName(link);
                            var filePath = Path.Combine("DownloadedFiles", fileName);

                            // Ist Datei bereits heruntergeladen?
                            if (!File.Exists(filePath) || IsFileChanged(absoluteUrl, filePath))
                            {
                                // Archivordner mit aktuellem Zeitstempel als Namen.
                                string archiveFolderName = DateTime.Now.ToString("yyyyMMdd");
                                string archiveFolderPath = Path.Combine(Directory.GetCurrentDirectory(), "Archives", archiveFolderName);

                                // Erstellen, wenn nicht vorhanden.
                                Directory.CreateDirectory(archiveFolderPath);

                                // Aktuelle Dateien ins Archiv
                                if (File.Exists(filePath))
                                {
                                    string archiveFilePath = Path.Combine(archiveFolderPath, fileName);
                                    File.Move(filePath, archiveFilePath);
                                    _logger.LogInformation($"Datei {fileName} wurde archiviert in {archiveFolderPath}.");
                                }

                                // Herunterladen der Dateien.
                                var fileResponse = await client.GetAsync(absoluteUrl, stoppingToken);
                                fileResponse.EnsureSuccessStatusCode();

                                using (var fileStream = File.Create(filePath))
                                {
                                    await fileResponse.Content.CopyToAsync(fileStream);
                                    _logger.LogInformation($"Datei {fileName} wurde heruntergeladen.");
                                }

                                // Informationen aktualisieren.
                                downloadedFilesInfo[fileName] = (new FileInfo(filePath).Length, DateTime.Now);
                            }
                            else
                            {
                                _logger.LogInformation($"Datei {fileName} ist noch aktuell, der Download wird übersprungen.");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Beim Crawl/Download ist ein Fehler aufgetreten.");
                }

                await Task.Delay(3600000, stoppingToken); // Worker läuft einmal pro Stunde.
            }
        }

        private bool IsFileChanged(string url, string localPath)
        {
            if (downloadedFilesInfo.TryGetValue(Path.GetFileName(localPath), out var fileInfo))
            {
                // Auf Dateiänderung prüfen.
                using (var client = new HttpClient())
                {
                    var headRequest = new HttpRequestMessage(HttpMethod.Head, url);
                    var response = client.SendAsync(headRequest).Result;

                    if (response.Headers.TryGetValues("Last-Modified", out var lastModifiedValues) &&
                        DateTime.TryParse(lastModifiedValues.First(), out var remoteLastModified))
                    {
                        return remoteLastModified > fileInfo.LastModified || new FileInfo(localPath).Length != fileInfo.Size;
                    }
                }
            }

            return true; // Aktuelle Online-Dateien sind nicht in den bekannten Daten enthalten, neuer Download resultiert.
        }
    }
}
