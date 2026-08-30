using System.CommandLine;
using InkLocaliser;
using DryDB.Compression;

// ----- Options -----
var retagOption = new Option<bool>("--retag")
{
    Description = "Regenerate all localisation tag IDs, rather than keep old IDs.",
};

var folderOption = new Option<string>("--folder")
{
    Description = "Root folder to scan for Ink files to localise, relative to working dir.",
    DefaultValueFactory = _ => "",
};

var filePatternOption = new Option<string>("--filePattern")
{
    Description = "File pattern for Ink files to localise.",
    DefaultValueFactory = _ => "*.ink",
};

var csvOption = new Option<string>("--csv")
{
    Description = "Path to a CSV folder to export. Default: no CSV file will be exported.",
    DefaultValueFactory = _ => "",
};

var jsonOption = new Option<string>("--json")
{
    Description = "Path to a JSON folder to export. Default: no JSON file will be exported.",
    DefaultValueFactory = _ => "",
};

var drydbOption = new Option<string>("--drydb")
{
    Description = "Path to a DryDB (.drydb) output folder. Default: no DryDB file will be exported.",
    DefaultValueFactory = _ => "",
};

var drydbNoCompressOption = new Option<bool>("--drydb-no-compress")
{
    Description = "Disable page compression for DryDB binary files.",
};

var drydbTablePrefixOption = new Option<string>("--drydb-table-prefix")
{
    Description = "Add a prefix to all table names in the DryDB database.",
    DefaultValueFactory = _ => "",
};

var drydbCsvOption = new Option<string>("--drydb-csv")
{
    Description = "Scan a folder for CSV files and convert each to .drydb.",
    DefaultValueFactory = _ => "",
};

var drydbCsvOutOption = new Option<string>("--drydb-csv-out")
{
    Description = "Optional output folder for converted .drydb files.",
    DefaultValueFactory = _ => "",
};

var onlyCsvToDrydbOption = new Option<bool>("--only-csv-to-drydb")
{
    Description = "Only run CSV->.drydb conversion and exit (skip Localiser run).",
};

var rootCommand = new RootCommand("InkTagger - localise Ink files by tagging strings and exporting them.");
rootCommand.Options.Add(retagOption);
rootCommand.Options.Add(folderOption);
rootCommand.Options.Add(filePatternOption);
rootCommand.Options.Add(csvOption);
rootCommand.Options.Add(jsonOption);
rootCommand.Options.Add(drydbOption);
rootCommand.Options.Add(drydbNoCompressOption);
rootCommand.Options.Add(drydbTablePrefixOption);
rootCommand.Options.Add(drydbCsvOption);
rootCommand.Options.Add(drydbCsvOutOption);
rootCommand.Options.Add(onlyCsvToDrydbOption);

// ----- CSV -> DryDB conversion helper -----
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

// ----- Action -----
rootCommand.SetAction(async (parseResult, cancellationToken) => {
    var options = new Localiser.Options {
        retag = parseResult.GetValue(retagOption),
        folder = parseResult.GetValue(folderOption) ?? "",
        filePattern = parseResult.GetValue(filePatternOption) ?? "*.ink",
    };
    var csvOptions = new CSVHandler.Options {
        outputFilePath = parseResult.GetValue(csvOption) ?? "",
    };
    var jsonOptions = new JSONHandler.Options {
        outputFilePath = parseResult.GetValue(jsonOption) ?? "",
    };
    var dryDBOptions = new DryDBHandler.Options {
        outputFilePath = parseResult.GetValue(drydbOption) ?? "",
        compress = !parseResult.GetValue(drydbNoCompressOption),
        tablePrefix = parseResult.GetValue(drydbTablePrefixOption) ?? "",
    };
    var dryDBCsvInput = parseResult.GetValue(drydbCsvOption) ?? "";
    var dryDBCsvOutput = parseResult.GetValue(drydbCsvOutOption) ?? "";
    var onlyCsvToDryDB = parseResult.GetValue(onlyCsvToDrydbOption);

    // If user requested only CSV->DryDB conversion, perform it now and exit.
    if (onlyCsvToDryDB) {
        if (string.IsNullOrWhiteSpace(dryDBCsvInput)) {
            Console.Error.WriteLine("--only-csv-to-drydb requires --drydb-csv=<folder> to be specified.");
            return 1;
        }

        var inputFolder = dryDBCsvInput;
        var outputFolder = string.IsNullOrWhiteSpace(dryDBCsvOutput) ? dryDBCsvInput : dryDBCsvOutput;
        if (!await ConvertCsvFolderAsync(inputFolder, outputFolder, dryDBOptions.compress)) {
            return 1;
        }
        return 0;
    }

    // ----- Parse Ink, Update Tags, Build String List -----
    var localiser = new Localiser(options);
    if (!localiser.Run()) {
        Console.Error.WriteLine("Not localised.");
        return 1;
    }
    Console.WriteLine($"Localised - found {localiser.GetStringKeys().Count} strings.");

    // ----- CSV Output -----
    if (!string.IsNullOrEmpty(csvOptions.outputFilePath)) {
        var csvHandler = new CSVHandler(localiser, csvOptions);
        if (!csvHandler.WriteStrings()) {
            Console.Error.WriteLine("Database not written.");
            return 1;
        }
    }

    // ----- JSON Output -----
    if (!string.IsNullOrEmpty(jsonOptions.outputFilePath)) {
        var jsonHandler = new JSONHandler(localiser, jsonOptions);
        if (!jsonHandler.WriteStrings()) {
            Console.Error.WriteLine("Database not written.");
            return 1;
        }
    }

    // ----- DryDB Binary Output -----
    if (!string.IsNullOrEmpty(dryDBOptions.outputFilePath)) {
        var dryDBHandler = new DryDBHandler(localiser, dryDBOptions);
        if (!dryDBHandler.WriteStrings()) {
            Console.Error.WriteLine("DryDB binary file not written.");
            return 1;
        }
    }

    // ----- CSV -> DryDB .drydb Conversion -----
    if (!string.IsNullOrEmpty(dryDBCsvInput)) {
        var inputFolder = dryDBCsvInput;
        var outputFolder = string.IsNullOrWhiteSpace(dryDBCsvOutput) ? dryDBCsvInput : dryDBCsvOutput;

        if (!await ConvertCsvFolderAsync(inputFolder, outputFolder, dryDBOptions.compress)) {
            return 1;
        }
    }

    return 0;
});

return rootCommand.Parse(args).Invoke();
