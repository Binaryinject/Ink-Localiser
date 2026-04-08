// Unity DryDB Usage Example
// This file shows how to correctly read a DryDB database in Unity
// DryDB repository: https://github.com/hadashiA/DryDB

using System.Text;
using UnityEngine;
using DryDB;
using Cysharp.Threading.Tasks;

public class DryDBLocalizationExample : MonoBehaviour
{
    private ReadOnlyDatabase database;
    private ReadOnlyTable demoTable;
    
    async UniTask Start()
    {
        // 1. Load the DryDB database file (without compression for Unity)
        var dryDBPath = System.IO.Path.Combine(Application.streamingAssetsPath, "strings.drydb");
        database = await ReadOnlyDatabase.OpenFileAsync(dryDBPath);
        
        // 2. Get a specific table (e.g., "demo", "test", etc.)
        demoTable = database.GetTable("demo");
        
        // 3. Query strings by key
        var text = GetLocalizedString("some_key_id");
        Debug.Log(text);
    }
    
    // Correct way to query: Use STRING key, not byte array
    public string GetLocalizedString(string key)
    {
        try
        {
            // IMPORTANT: Use string key directly, NOT Encoding.UTF8.GetBytes(key)
            // The database was created with KeyEncoding.Ascii, so keys must be strings
            var valueBytes = demoTable.Get(key);
            
            if (valueBytes == null || valueBytes.Length == 0)
            {
                Debug.LogWarning($"Localization key not found: {key}");
                return key;
            }
            
            // Values are stored as UTF-8 bytes
            return Encoding.UTF8.GetString(valueBytes);
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Error getting localized string for key '{key}': {ex.Message}");
            return key;
        }
    }
    
    void OnDestroy()
    {
        database?.Dispose();
    }
}

/* 
 * COMMON MISTAKES TO AVOID:
 * 
 * ❌ WRONG - This will cause NullReferenceException:
 *    var keyBytes = Encoding.UTF8.GetBytes(key);
 *    var valueBytes = table.Get(keyBytes);
 * 
 * ✅ CORRECT - Use string key:
 *    var valueBytes = table.Get(key);
 * 
 * 
 * GENERATING DryDB FOR UNITY:
 * 
 * - Without compression (recommended for Unity):
 *   dotnet run -- --folder=tests --drydb=output --drydb-no-compress
 * 
 * - With compression (enabled by default, requires DryDB.Compression in Unity):
 *   dotnet run -- --folder=tests --drydb=output
 *   Note: Unity project must also install DryDB.Compression package
 * 
 * - With table prefix:
 *   dotnet run -- --folder=tests --drydb=output --drydb-table-prefix=loc_
 *   Then access as: database.GetTable("loc_demo")
 */
