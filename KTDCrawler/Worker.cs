using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using HtmlAgilityPack;

namespace KTDCrawler
{
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;
        private Dictionary<string, (long Size, DateTime LastModified, string Checksum)> downloadedFilesInfo = new Dictionary<string, (long, DateTime, string)>();

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

                        // Gesuchte Dateiendungen der Kostenträgerdateien
                        var fileExtensions = new List<string> { ".ke0", ".ke1", ".ke2", ".ke3", ".ke4", ".ke5", ".ke6", ".ke7", ".ke8", ".ke9" };

                        // Alle Links mit den relevanten Dateiendungen erfassen
                        var ke0Links = htmlDocument.DocumentNode
                            .Descendants("a")
                            .Where(a => a.Attributes["href"] != null && fileExtensions.Any(ext => a.Attributes["href"].Value.EndsWith(ext)))
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

                            // Ist (neueste) Datei bereits heruntergeladen?
                            if (!File.Exists(filePath) || IsFileChanged(filePath))
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
                                downloadedFilesInfo[fileName] = (new FileInfo(filePath).Length, DateTime.Now, ComputeFileHash(filePath));
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

        private bool IsFileChanged(string localPath)
        {
            if (downloadedFilesInfo.TryGetValue(Path.GetFileName(localPath), out var fileInfo))
            {
                string currentHash = ComputeFileHash(localPath);

                if (currentHash != fileInfo.Checksum)
                {
                    return true; // Andere Checksum -> Neue Datei
                }
            }

            return false; // Gleiche Checksum oder Datei nicht im Dictionary
        }

        private string ComputeFileHash(string filePath)
        {
            using (var sha256 = SHA256.Create())
            {
                using (var stream = File.OpenRead(filePath))
                {
                    byte[] hash = sha256.ComputeHash(stream);
                    return BitConverter.ToString(hash).Replace("-", "").ToLower();
                }
            }
        }
    }
}
