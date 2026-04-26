using CsvHelper;
using CsvHelper.Configuration;
using GISSpatialChecker.Models;
using OfficeOpenXml;
using System.Globalization;
using System.IO;

namespace GISSpatialChecker.Services
{
    public static class FileService
    {
        static FileService()
        {
            // EPPlus lisensi non-commercial (gratis)
            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
        }

        // ─── IMPORT CSV ───────────────────────────────────────────
        public static List<EntityPoint> ImportCsv(string filePath, string group)
        {
            var list = new List<EntityPoint>();

            // Auto-detect delimiter: baca baris pertama
            string firstLine = "";
            using (var sr = new StreamReader(filePath, System.Text.Encoding.UTF8))
                firstLine = sr.ReadLine() ?? "";

            // Hapus BOM character jika ada
            firstLine = firstLine.TrimStart('\uFEFF', '\u200B');
            char delimiter = firstLine.Contains(';') ? ';' : ',';

            var config = new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                HeaderValidated = null!,
                MissingFieldFound = null!,
                BadDataFound = null!,
                Delimiter = delimiter.ToString(),
                Encoding = System.Text.Encoding.UTF8,
                TrimOptions = TrimOptions.Trim, // Auto trim spasi
                PrepareHeaderForMatch = args => args.Header.Trim().TrimStart('\uFEFF').ToLower()
            };

            using var reader = new StreamReader(filePath, System.Text.Encoding.UTF8);
            using var csv = new CsvReader(reader, config);

            csv.Read();
            csv.ReadHeader();

            int index = 1;
            while (csv.Read())
            {
                try
                {
                    // Baca dan bersihkan nilai latitude/longitude
                    var latStr = (csv.TryGetField("latitude", out string? ls) ? ls : "") ?? "";
                    var lonStr = (csv.TryGetField("longitude", out string? lo) ? lo : "") ?? "";

                    latStr = latStr.Trim().Replace(",", ".");
                    lonStr = lonStr.Trim().Replace(",", ".");

                    if (!double.TryParse(latStr, NumberStyles.Any, CultureInfo.InvariantCulture, out double lat)) continue;
                    if (!double.TryParse(lonStr, NumberStyles.Any, CultureInfo.InvariantCulture, out double lon)) continue;

                    var point = new EntityPoint
                    {
                        Id = (csv.TryGetField("id", out string? id) ? id?.Trim() : null) is string sid && !string.IsNullOrEmpty(sid)
                            ? sid : $"{group}{index}",
                        Name = (csv.TryGetField("name", out string? name) ? name?.Trim() : null) is string sname && !string.IsNullOrEmpty(sname)
                            ? sname : $"Entity {group}{index}",
                        Latitude = lat,
                        Longitude = lon,
                        Group = group,
                        Description = (csv.TryGetField("description", out string? desc) ? desc?.Trim() : "") ?? ""
                    };
                    list.Add(point);
                    index++;
                }
                catch { /* skip baris error */ }
            }

            return list;
        }

        // ─── IMPORT EXCEL ─────────────────────────────────────────
        public static List<EntityPoint> ImportExcel(string filePath, string group)
        {
            var list = new List<EntityPoint>();

            using var package = new ExcelPackage(new FileInfo(filePath));
            var sheet = package.Workbook.Worksheets[0];

            if (sheet == null || sheet.Dimension == null) return list;

            // Baca header baris 1
            var headers = new Dictionary<string, int>();
            for (int col = 1; col <= sheet.Dimension.End.Column; col++)
            {
                var header = sheet.Cells[1, col].Text.Trim().ToLower();
                headers[header] = col;
            }

            int index = 1;
            for (int row = 2; row <= sheet.Dimension.End.Row; row++)
            {
                try
                {
                    var latText = GetCellText(sheet, row, headers, "latitude");
                    var lonText = GetCellText(sheet, row, headers, "longitude");

                    if (string.IsNullOrEmpty(latText) || string.IsNullOrEmpty(lonText)) continue;

                    if (!double.TryParse(latText, NumberStyles.Any, CultureInfo.InvariantCulture, out double lat)) continue;
                    if (!double.TryParse(lonText, NumberStyles.Any, CultureInfo.InvariantCulture, out double lon)) continue;

                    var point = new EntityPoint
                    {
                        Id = GetCellText(sheet, row, headers, "id") is string sid && !string.IsNullOrEmpty(sid)
                            ? sid : $"{group}{index}",
                        Name = GetCellText(sheet, row, headers, "name") is string sname && !string.IsNullOrEmpty(sname)
                            ? sname : $"Entity {group}{index}",
                        Latitude = lat,
                        Longitude = lon,
                        Group = group,
                        Description = GetCellText(sheet, row, headers, "description") ?? ""
                    };

                    list.Add(point);
                    index++;
                }
                catch { /* skip baris error */ }
            }

            return list;
        }

        private static string? GetCellText(ExcelWorksheet sheet, int row, Dictionary<string, int> headers, string key)
        {
            return headers.TryGetValue(key, out int col)
                ? sheet.Cells[row, col].Text.Trim()
                : null;
        }

        // ─── EXPORT CSV ───────────────────────────────────────────
        public static void ExportCsv(List<SpatialResult> results, string filePath)
        {
            using var writer = new StreamWriter(filePath, false, System.Text.Encoding.UTF8);
            using var csv = new CsvWriter(writer, CultureInfo.InvariantCulture);

            // Header
            csv.WriteField("Entity A");
            csv.WriteField("Lat A");
            csv.WriteField("Lon A");
            csv.WriteField("Entity B");
            csv.WriteField("Lat B");
            csv.WriteField("Lon B");
            csv.WriteField("Jarak (m)");
            csv.WriteField("Radius Setting (m)");
            csv.WriteField("Status");
            csv.NextRecord();

            // Data
            foreach (var r in results)
            {
                csv.WriteField(r.EntityAName);
                csv.WriteField(r.EntityALat);
                csv.WriteField(r.EntityALon);
                csv.WriteField(r.EntityBName);
                csv.WriteField(r.EntityBLat);
                csv.WriteField(r.EntityBLon);
                csv.WriteField(r.JarakMeter.ToString("F2"));
                csv.WriteField(r.RadiusSetting);
                csv.WriteField(r.DalamRadius ? "DALAM" : "LUAR");
                csv.NextRecord();
            }
        }

        // ─── EXPORT EXCEL ─────────────────────────────────────────
        public static void ExportExcel(List<SpatialResult> results, string filePath)
        {
            using var package = new ExcelPackage();
            var sheet = package.Workbook.Worksheets.Add("Hasil Spatial Check");

            // Header style
            var headers = new[] { "Entity A", "Lat A", "Lon A", "Entity B", "Lat B", "Lon B", "Jarak (m)", "Radius (m)", "Status" };
            for (int i = 0; i < headers.Length; i++)
            {
                var cell = sheet.Cells[1, i + 1];
                cell.Value = headers[i];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.PatternType = OfficeOpenXml.Style.ExcelFillStyle.Solid;
                cell.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(70, 130, 180));
                cell.Style.Font.Color.SetColor(System.Drawing.Color.White);
            }

            // Data rows
            for (int i = 0; i < results.Count; i++)
            {
                var r = results[i];
                int row = i + 2;

                sheet.Cells[row, 1].Value = r.EntityAName;
                sheet.Cells[row, 2].Value = r.EntityALat;
                sheet.Cells[row, 3].Value = r.EntityALon;
                sheet.Cells[row, 4].Value = r.EntityBName;
                sheet.Cells[row, 5].Value = r.EntityBLat;
                sheet.Cells[row, 6].Value = r.EntityBLon;
                sheet.Cells[row, 7].Value = Math.Round(r.JarakMeter, 2);
                sheet.Cells[row, 8].Value = r.RadiusSetting;
                sheet.Cells[row, 9].Value = r.DalamRadius ? "DALAM" : "LUAR";

                // Warna baris berdasarkan status
                var color = r.DalamRadius
                    ? System.Drawing.Color.FromArgb(198, 239, 206)  // Hijau muda
                    : System.Drawing.Color.FromArgb(255, 199, 206); // Merah muda

                for (int col = 1; col <= 9; col++)
                {
                    sheet.Cells[row, col].Style.Fill.PatternType = OfficeOpenXml.Style.ExcelFillStyle.Solid;
                    sheet.Cells[row, col].Style.Fill.BackgroundColor.SetColor(color);
                }
            }

            sheet.Cells[sheet.Dimension.Address].AutoFitColumns();
            package.SaveAs(new FileInfo(filePath));
        }

        // ─── TEMPLATE CSV ─────────────────────────────────────────
        public static void BuatTemplateCsv(string filePath)
        {
            using var writer = new StreamWriter(filePath, false, System.Text.Encoding.UTF8);
            writer.WriteLine("Id,Name,Latitude,Longitude,Description");
            writer.WriteLine("A1,Entity A1,-7.3313865026188445,112.73283711961221,Contoh titik 1");
            writer.WriteLine("A2,Entity A2,-7.3308331638129935,112.73286930611975,Contoh titik 2");
        }
    }
}
