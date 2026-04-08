using System;
using System.IO;
using System.Text;
using DryDB;
using DryDB.Compression;

namespace InkLocaliser
{
    public class DryDBHandler(Localiser localiser, DryDBHandler.Options options) {

        public class Options {
            public string outputFilePath = "";
            public bool compress = true;
            public string tablePrefix = ""; // DryDB table prefix
        }

        /// <summary>
        /// Writes strings as DryDB binary format.
        /// Uses the DryDB library to convert strings to an optimized B+Tree based key/value database.
        /// All strings are merged into a single .drydb file with multiple tables (one table per source file).
        /// The format supports:
        /// - B+Tree based efficient key-value lookup
        /// - Multiple tables in one database file
        /// - Zstandard page compression (when enabled)
        /// - Read-only embedded database format
        /// </summary>
        public async Task<bool> WriteStringsAsync() {
            try {
                if (!Directory.Exists(options.outputFilePath)) 
                    Directory.CreateDirectory(options.outputFilePath);

                var outputs = new Dictionary<string, Dictionary<string, string>>();

                // Group strings by path
                foreach (var locID in localiser.GetStringKeys()) {
                    var path = localiser.GetStringPath(locID);
                    if (!outputs.TryGetValue(path, out var output)) {
                        output = new Dictionary<string, string>();
                        outputs.Add(path, output);
                    }
                    output.Add(locID, localiser.GetString(locID));
                }

                // Create a single DryDB database file with multiple tables
                var dryDBFilePath = Path.Combine(options.outputFilePath, "strings.drydb");
                
                try {
                    // Create DryDB database builder
                    var builder = new DatabaseBuilder
                    {
                        PageSize = 4096,
                    };

                    // Add Zstandard compression filter if enabled
                    if (options.compress) {
                        builder.AddPageFilter(x => {
                            x.AddZstandardCompression();
                        });
                        Console.WriteLine("Zstandard compression enabled");
                    }

                    // Create a table for each source file
                    // Using KeyEncoding.Ascii - Unity should query with string keys, not byte arrays
                    foreach (var output in outputs) {
                        var tableName = Path.GetFileNameWithoutExtension(output.Key);
                        
                        // Add prefix to table name if specified
                        if (!string.IsNullOrEmpty(options.tablePrefix)) {
                            tableName = options.tablePrefix + tableName;
                        }
                        
                        var table = builder.CreateTable(tableName, KeyEncoding.Ascii);
                        
                        // Append all key-value pairs for this table
                        // Keys: string (Ascii encoding)
                        // Values: UTF-8 bytes
                        foreach (var kvp in output.Value) {
                            var keyBytes = Encoding.UTF8.GetBytes(kvp.Key);
                            var valueBytes = Encoding.UTF8.GetBytes(kvp.Value);
                            table.Append(keyBytes, valueBytes);
                        }
                        
                        Console.WriteLine($"Added table '{tableName}' with {output.Value.Count} entries");
                    }

                    // Build to single file
                    await builder.BuildToFileAsync(dryDBFilePath);

                    Console.WriteLine($"DryDB database written: {dryDBFilePath} (contains {outputs.Count} tables)");
                }
                catch (Exception ex) {
                    Console.Error.WriteLine($"Error writing DryDB database {dryDBFilePath}: {ex.Message}");
                    throw;
                }
            }
            catch (Exception ex) {
                Console.Error.WriteLine($"Error writing out DryDB database: {options.outputFilePath}: " + ex.Message);
                return false;
            }

            return true;
        }

        /// <summary>
        /// Synchronous wrapper for WriteStringsAsync
        /// </summary>
        public bool WriteStrings() {
            return WriteStringsAsync().GetAwaiter().GetResult();
        }
    }
}
