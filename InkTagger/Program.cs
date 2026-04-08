using InkLocaliser;
using DryDB.Compression;

var options = new Localiser.Options();
var csvOptions = new CSVHandler.Options();
var jsonOptions = new JSONHandler.Options();
var dryDBOptions = new DryDBHandler.Options();
var dryDBCsvInput = "";
var dryDBCsvOutput = "";
var onlyCsvToDryDB = false;

// ----- Simple Args -----
foreach (var arg in args)
{
    if (arg.Equals("--retag"))
        options.retag = true;
    else if (arg.StartsWith("--folder="))
        options.folder = arg.Substring(9);
    else if (arg.StartsWith("--filePattern="))
        options.filePattern = arg.Substring(14);
    else if (arg.StartsWith("--csv="))
        csvOptions.outputFilePath = arg.Substring(6);
    else if (arg.StartsWith("--json="))
        jsonOptions.outputFilePath = arg.Substring(7);
    else if (arg.StartsWith("--drydb="))
        dryDBOptions.outputFilePath = arg.Substring(9);
    else if (arg.Equals("--drydb-no-compress"))
        dryDBOptions.compress = false;
    else if (arg.StartsWith("--drydb-table-prefix="))
        dryDBOptions.tablePrefix = arg.Substring(22);
    else if (arg.StartsWith("--drydb-csv="))
        dryDBCsvInput = arg.Substring(13);
    else if (arg.StartsWith("--drydb-csv-out="))
        dryDBCsvOutput = arg.Substring(17);
    else if (arg.Equals("--only-csv-to-drydb"))
        onlyCsvToDryDB = true;
    else if (arg.Equals("--help") || arg.Equals("-h")) {
        Console.WriteLine("InkTagger");
        Console.WriteLine("Arguments:");
        Console.WriteLine("  --folder=<folder> - Root folder to scan for Ink files to localise, relative to working dir.");
        Console.WriteLine("                      e.g. --folder=inkFiles/");
        Console.WriteLine("                      Default is the current working dir.");
        Console.WriteLine("  --filePattern=<folder> - Root folder to scan for Ink files to localise.");
        Console.WriteLine("                           e.g. --filePattern=start-*.ink");
        Console.WriteLine("                           Default is *.ink");
        Console.WriteLine("  --csv - Path to a CSV folder to export");
        Console.WriteLine("                    Default is empty, so no CSV file will be exported.");
        Console.WriteLine("  --json - Path to a JSON folder to export");
        Console.WriteLine("                      Default is empty, so no JSON file will be exported.");
        Console.WriteLine("  --drydb - Path to a DryDB (.drydb) output folder");
        Console.WriteLine("                       Default is empty, so no DryDB file will be exported.");
        Console.WriteLine("                       DryDB files use a B+Tree based key-value database format.");
        Console.WriteLine("  --drydb-no-compress - Disable page compression for DryDB binary files.");
        Console.WriteLine("                           Use with --drydb parameter.");
        Console.WriteLine("  --drydb-table-prefix=<prefix> - Add prefix to all table names in the DryDB database.");
        Console.WriteLine("                                     Example: --drydb-table-prefix=loc_");
        Console.WriteLine("  --drydb-csv=<folder> - Scan a folder for CSV files and convert each to .drydb");
        Console.WriteLine("                           Example: --drydb-csv=translations/csvs");
        Console.WriteLine("  --drydb-csv-out=<folder> - Optional output folder for converted .drydb files.");
        Console.WriteLine("  --only-csv-to-drydb - Only run CSV->.drydb conversion and exit (skip Localiser run).");
        Console.WriteLine("  --retag - Regenerate all localisation tag IDs, rather than keep old IDs.");
        return 0;
    }
}

// Local function to convert a CSV folder to DryDB .drydb files
async Task<bool> ConvertCsvFolderAsync(string inputFolder, string outputFolder, bool compress) {
    try {
        if (!System.IO.Directory.Exists(inputFolder)) {
            Console.Error.WriteLine($"CSV input folder does not exist: {inputFolder}");
            return false;
        }
        if (!System.IO.Directory.Exists(outputFolder)) System.IO.Directory.CreateDirectory(outputFolder);

        // Collect all CSV files
        var csvFiles = System.IO.Directory.GetFiles(inputFolder, "*.csv", System.IO.SearchOption.AllDirectories);
        if (csvFiles.Length == 0) {
            Console.WriteLine("No CSV files found.");
            return true;
        }

        // Create a single DryDB database with multiple tables
        var dryDBFilePath = System.IO.Path.Combine(outputFolder, "strings.drydb");
        var builder = new DryDB.DatabaseBuilder { PageSize = 4096 };

        // Add Zstandard compression if enabled
        if (compress) {
            builder.AddPageFilter(x => {
                x.AddZstandardCompression();
            });
            Console.WriteLine("Zstandard compression enabled");
        }

        foreach (var csvFile in csvFiles) {
            try {
                var fileName = System.IO.Path.GetFileNameWithoutExtension(csvFile);
                var relativePath = System.IO.Path.GetRelativePath(inputFolder, csvFile);
                var relativeDir = System.IO.Path.GetDirectoryName(relativePath);
                
                // Create table name from relative path (replace path separators with underscores)
                var tableName = string.IsNullOrEmpty(relativeDir) 
                    ? fileName 
                    : $"{relativeDir.Replace("\\", "_").Replace("/", "_")}_{fileName}";

                var table = builder.CreateTable(tableName, DryDB.KeyEncoding.Ascii);
                
                // Read and parse CSV file
                using (var reader = new System.IO.StreamReader(csvFile)) {
                    var headerLine = await reader.ReadLineAsync();
                    // Skip header line (ID,Text)
                    
                    int entryCount = 0;
                    while (!reader.EndOfStream) {
                        var line = await reader.ReadLineAsync();
                        if (string.IsNullOrWhiteSpace(line)) continue;
                        
                        // Simple CSV parsing (handles quoted values)
                        var parts = ParseCsvLine(line);
                        if (parts.Length >= 2) {
                            var key = parts[0];
                            var value = parts[1];
                            var valueBytes = System.Text.Encoding.UTF8.GetBytes(value);
                            table.Append(key, valueBytes);
                            entryCount++;
                        }
                    }
                    Console.WriteLine($"Added table '{tableName}' from {csvFile} ({entryCount} entries)");
                }
            }
            catch (Exception ex) {
                Console.Error.WriteLine($"Error processing {csvFile}: {ex.Message}");
                return false;
            }
        }

        // Build the final DryDB database
        await builder.BuildToFileAsync(dryDBFilePath);
        Console.WriteLine($"Converted {csvFiles.Length} CSV files to {dryDBFilePath}");
    }
    catch (Exception ex) {
        Console.Error.WriteLine($"Error scanning CSV folder: {ex.Message}");
        return false;
    }

    return true;
}

// Simple CSV line parser that handles quoted fields
string[] ParseCsvLine(string line) {
    var result = new List<string>();
    var current = new System.Text.StringBuilder();
    var inQuotes = false;
    
    for (int i = 0; i < line.Length; i++) {
        var c = line[i];
        
        if (c == '"') {
            if (inQuotes && i + 1 < line.Length && line[i + 1] == '"') {
                current.Append('"');
                i++; // Skip next quote
            } else {
                inQuotes = !inQuotes;
            }
        } else if (c == ',' && !inQuotes) {
            result.Add(current.ToString());
            current.Clear();
        } else {
            current.Append(c);
        }
    }
    
    result.Add(current.ToString());
    return result.ToArray();
}

// If user requested only CSV->DryDB conversion, perform it now and exit.
if (onlyCsvToDryDB)
{
    if (string.IsNullOrWhiteSpace(dryDBCsvInput)) {
        Console.Error.WriteLine("--only-csv-to-drydb requires --drydb-csv=<folder> to be specified.");
        return -1;
    }

    var inputFolder = dryDBCsvInput;
    var outputFolder = string.IsNullOrWhiteSpace(dryDBCsvOutput) ? dryDBCsvInput : dryDBCsvOutput;
    if (!await ConvertCsvFolderAsync(inputFolder, outputFolder, dryDBOptions.compress)) return -1;
    return 0;
}

// ----- Parse Ink, Update Tags, Build String List -----
var localiser = new Localiser(options);
if (!localiser.Run()) {
    Console.Error.WriteLine("Not localised.");
    return -1;
}
Console.WriteLine($"Localised - found {localiser.GetStringKeys().Count} strings.");

// ----- CSV Output -----
if (!string.IsNullOrEmpty(csvOptions.outputFilePath))
{
    var csvHandler = new CSVHandler(localiser, csvOptions);
    if (!csvHandler.WriteStrings()) {
        Console.Error.WriteLine("Database not written.");
        return -1;
    }
}

// ----- JSON Output -----
if (!string.IsNullOrEmpty(jsonOptions.outputFilePath))
{
    var jsonHandler = new JSONHandler(localiser, jsonOptions);
    if (!jsonHandler.WriteStrings()) {
        Console.Error.WriteLine("Database not written.");
        return -1;
    }
}

// ----- DryDB Binary Output -----
if (!string.IsNullOrEmpty(dryDBOptions.outputFilePath))
{
    var dryDBHandler = new DryDBHandler(localiser, dryDBOptions);
    if (!dryDBHandler.WriteStrings()) {
        Console.Error.WriteLine("DryDB binary file not written.");
        return -1;
    }
}

// ----- CSV -> DryDB .drydb Conversion -----
if (!string.IsNullOrEmpty(dryDBCsvInput))
{
    var inputFolder = dryDBCsvInput;
    var outputFolder = string.IsNullOrWhiteSpace(dryDBCsvOutput) ? dryDBCsvInput : dryDBCsvOutput;

    if (!await ConvertCsvFolderAsync(inputFolder, outputFolder, dryDBOptions.compress)) return -1;
}

return 0;
