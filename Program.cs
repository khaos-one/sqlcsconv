using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text.RegularExpressions;
using CommandLine;
using MySql.Data.MySqlClient;

namespace sqlcsconv {
    class Program {
        static readonly Options Options = new Options();
        static string Database = null;
        static string Table = null;
        static MySqlConnection Connection;

        static void Fail(string message = null, int exitCode = 1) {
            if (!string.IsNullOrWhiteSpace(message)) {
                Console.Write("ERROR: ");
                Console.WriteLine(message);
            }
            else {
                Console.WriteLine(Options.GetUsage());
            }

            Connection?.Dispose();
            Environment.Exit(exitCode);
        }

        static List<T> SelectColumn<T>(string cmdText, int columnNumber = 0) {
            var result = new List<T>();
            using (var cmd = Connection.CreateCommand()) {
                cmd.CommandText = cmdText;
                using (var r = cmd.ExecuteReader()) {
                    while (r.Read()) {
                        result.Add((T)r[columnNumber]);
                    }
                }
            }

            return result;
        }

        static DataSet Select(string cmd) {
            var result = new DataSet();
            using (var adapter = new MySqlDataAdapter(cmd, Connection)) {
                adapter.Fill(result);
            }

            return result;
        }

        static DataTable SelectTable(string cmd, int tableNumber = 0) {
            return Select(cmd).Tables[tableNumber];
        }

        static void Main(string[] args) {
            // Parse arguments.
            if (!Parser.Default.ParseArguments(args, Options)) {
                Environment.Exit(1);
            }

            // Test for database/table names in target.
            var matches = Regex.Match(Options.Target, @"^`?([0-9a-zA-Z$_]+)`?\.`?([0-9a-zA-Z$_]+)`?$");

            if (!matches.Success) {
                matches = Regex.Match(Options.Target, "^`?([0-9a-zA-Z$_]+)`?$");

                if (!matches.Success) {
                    Fail("Invalid target name.");
                }
                else {
                    Database = matches.Groups[1].Value;
                }
            }
            else {
                Database = matches.Groups[1].Value;
                Table = matches.Groups[2].Value;
            }

            // Build connection string.
            var connectionString = $"Server={Options.Host};Port={Options.Port};Database={Database};Uid={Options.User}";

            if (Options.Password != null) {
                connectionString += $";Pwd={Options.Password}";
            }

            // Try to connect to database.
            try {
                Connection = new MySqlConnection(connectionString);
                Connection.Open();
            }
            catch (Exception e) {
                Fail(e.Message);
            }

            // Get DBMS supported encodings.
            var supportedEncodings = SelectColumn<string>("SHOW CHARACTER SET");

            if (!supportedEncodings.Contains(Options.DestEncoding)) {
                Fail($"Specified destination encoding '{Options.DestEncoding}' not supported by DBMS.");
            }

            if (Options.SourceEncoding != null) {
                if (!supportedEncodings.Contains(Options.SourceEncoding)) {
                    Fail($"Specified destination encoding '{Options.SourceEncoding}' not supported by DBMS.");
                }
            }

            // Seek out the database.
            var databases = SelectColumn<string>("SHOW DATABASES");

            if (!databases.Contains(Database)) {
                Fail($"Specified database {Database} does not exist.");
            }

            // Retrieve tables list for database.
            var tables = SelectColumn<string>($"SHOW TABLES IN `{Database}`");

            // If table was specified, check it does exist.
            if (Table != null) {
                if (!tables.Contains(Table)) {
                    Fail($"Specified target table '{Table}' does not exist.");
                }
            }

            Console.Write(CreateConversionScriptForTable(Table, Options.DestEncoding));
        }

        static string CreateConversionScriptForTable(string tbl, string destCharset, string sourceCharset = null) {
            var describe = SelectTable($"DESCRIBE `{Database}`.`{tbl}`");
            var columnsToConvert = new Dictionary<string, Tuple<string, string>>();

            // First, collect information about fields in the table.
            foreach (DataRow row in describe.Rows) {
                var rowName = row["Field"] as string;
                var rowType = row["Type"] as string;

                if (rowName == null || rowType == null) {
                    continue;
                }

                // Find out which columns to convert and how.
                // The rule is:
                //
                // CHAR ⇾ BINARY
                // TEXT ⇾ BLOB
                // TINYTEXT ⇾ TINYBLOB
                // MEDIUMTEXT ⇾ MEDIUMBLOB
                // LONGTEXT ⇾ LONGBLOB
                // VARCHAR ⇾ VARBINARY
                //
                // with respected sizes.

                var match = Regex.Match(rowType, @"^(?:VARCHAR|varchar)\((\d+)\)$");

                if (match.Success) {
                    columnsToConvert.Add(rowName, new Tuple<string, string>(match.Value, $"VARBINARY({match.Groups[1].Value})"));
                    continue;
                }

                match = Regex.Match(rowType, @"^(?:CHAR|char)\((\d+)\)$");

                if (match.Success) {
                    columnsToConvert.Add(rowName, new Tuple<string, string>(match.Value, $"BINARY({match.Groups[1].Value})"));
                    continue;
                }

                if (Regex.IsMatch(rowType, "^(?:TINYTEXT|tinytext)$")) {
                    columnsToConvert.Add(rowName, new Tuple<string, string>("TINYTEXT", "TINYBLOB"));
                    continue;
                }

                if (Regex.IsMatch(rowType, "^(?:MEDIUMTEXT|mediumtext)$")) {
                    columnsToConvert.Add(rowName, new Tuple<string, string>("MEDIUMTEXT", "MEDIUMBLOB"));
                    continue;
                }

                if (Regex.IsMatch(rowType, "^(?:LONGTEXT|longtext)$")) {
                    columnsToConvert.Add(rowName, new Tuple<string, string>("LONGTEXT", "LOGBLOB"));
                    continue;
                }

                if (Regex.IsMatch(rowType, "^(?:TEXT|text)$")) {
                    columnsToConvert.Add(rowName, new Tuple<string, string>("TEXT", "BLOB"));
                }
            }

            // We also need information about table indexes.
            // It's needed because *TEXT ot *BLOB conversion on the fields in index will violate the index.
            // So we need to drop them during conversion and recreate afterwards.
            var indexes = SelectTable($"SHOW INDEXES FROM `{Database}`.`{tbl}`");
            var indexesBuffer = new List<Tuple<string, string, string, long>>();
            var indexesToConvert = new Dictionary<string, Tuple<List<string>, string>>();

            foreach (DataRow row in indexes.Rows) {
                var indexName = row["Key_name"] as string;
                var indexColumnName = row["Column_name"] as string;
                var indexType = row["Index_type"] as string;
                var indexNonUnique = (long) row["Non_unique"];

                if (indexName == null || indexColumnName == null || indexType == null) {
                    continue;
                }

                if (indexNonUnique == 0) {
                    indexType = "UNIQUE";
                }
                else if (indexType != "FULLTEXT") {
                    indexType = null;
                }

                indexesBuffer.Add(new Tuple<string, string, string, long>(indexName, indexColumnName, indexType, indexNonUnique));

                foreach (var column in columnsToConvert) {
                    if (indexColumnName == column.Key &&
                        (column.Value.Item1 == "TINYTEXT" || column.Value.Item1 == "MEDIUMTEXT" ||
                         column.Value.Item1 == "LOGTEXT" || column.Value.Item1 == "TEXT") &&
                         !indexesToConvert.ContainsKey(indexName)) {
                        indexesToConvert.Add(indexName,
                            new Tuple<List<string>, string>(new List<string>(new[] {indexColumnName}), indexType));
                        break;
                    }
                }
            }

            foreach (var indexColumn in indexesBuffer) {
                if (columnsToConvert.ContainsKey(indexColumn.Item2)) {
                    if (!indexesToConvert[indexColumn.Item1].Item1.Contains(indexColumn.Item2)) {
                        indexesToConvert[indexColumn.Item1].Item1.Add(indexColumn.Item2);
                    }
                }
            }

            // Now assemble resulting SQL script.
            var alterTable = $"ALTER TABLE `{Database}`.`{tbl}`";
            var script = $"-- Table `{Database}`.`{tbl}`\n\n" +
                         $"{alterTable} CHARACTER SET {destCharset};\n";

            if (indexesToConvert.Any()) {
                script += $"{alterTable} \n";
                script = indexesToConvert.Aggregate(script, (current, index) => current + $"    DROP INDEX `{index.Key}`,\n");
                script = script.Remove(script.Length - 2);
                script += ";\n\n";
            }

            if (columnsToConvert.Any()) {
                script += $"{alterTable} \n";
                script = columnsToConvert.Aggregate(script,
                    (current, column) => current + $"    MODIFY `{column.Key}` {column.Value.Item2},\n");
                script = script.Remove(script.Length - 2);
                script += ";\n\n";

                script += $"{alterTable} \n";
                script = columnsToConvert.Aggregate(script,
                    (current, column) => current + $"    MODIFY `{column.Key}` {column.Value.Item1} CHARACTER SET {destCharset},\n");
                script = script.Remove(script.Length - 2);
                script += ";\n\n";
            }

            if (indexesToConvert.Any()) {
                script += $"{alterTable} \n";
                script = indexesToConvert.Aggregate(script, (current, index) => {
                    if (index.Value.Item2 != null) {
                        current += $"    ADD {index.Value.Item2} ";
                    }
                    else {
                        current += $"    ADD ";
                    }

                    current += $"INDEX `{index.Key}` (";
                    current = index.Value.Item1.Aggregate(current, (current1, column) => current1 + $"`{column}`,");
                    current = current.Remove(current.Length - 1);
                    return current + "),\n";
                });
                script = script.Remove(script.Length - 2);
                script += ";\n\n";
            }

            return script;
        }
    }
}
