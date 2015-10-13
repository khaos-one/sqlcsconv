using System;
using System.Collections.Generic;
using System.Data.SqlTypes;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommandLine;
using CommandLine.Text;

namespace sqlcsconv {
    class Options {
        [Option('h', "Host", DefaultValue = "localhost", HelpText = "DBMS host to connect to.", Required = false)]
        public string Host { get; set; }

        [Option("Port", DefaultValue = 3306, HelpText = "DBMS port to connect to.", Required = false)]
        public int Port { get; set; }

        [Option('u', "User", DefaultValue = "root", HelpText = "DBMS user to use during connection.", Required = false)]
        public string User { get; set; }

        [Option('p', "Password", DefaultValue = null, HelpText = "DBMS password for specified user.", Required = false)]
        public string Password { get; set; }

        [Option('t', "Target", Required = true, HelpText = "Target of conversation -- whole database or individual table. In the case of a single table a database where this table resides must be specified (i.e. 'database.table').")]
        public string Target { get; set; }
        
        [Option('s', "SourceEncoding", DefaultValue = null, HelpText = "Source encoding of the object(s) of conversion in SQL format (i.e. 'utf8'). If omitted, reinterpretation will be done.", Required = false)]
        public string SourceEncoding { get; set; }

        [Option('d', "DestEncoding", HelpText = "Destination encoding for the object(s) of conversion in SQL format (i.e. 'utf8').", Required = true)]
        public string DestEncoding { get; set; }
        
        [Option('g', "GenerateScript", DefaultValue = false, HelpText = "If specified, conversion SQL script will be outputted, database will not be changed.", Required = false, MutuallyExclusiveSet = "Verbose")]
        public bool GenerateScript { get; set; }

        [Option('v', "Verbose", DefaultValue = false, HelpText = "Sets the verbosity of the output.", Required = false, MutuallyExclusiveSet = "GenerateScript")]
        public bool Verbose { get; set; }

        [Option('i', "Imitate", DefaultValue = false, HelpText = "Only imitate applying changes, no actual conversion will be done.", Required = false)]
        public bool Imitate { get; set; }

        [Option('e', "ExcludeTables", DefaultValue = null, HelpText = "Exclude following tables when performing batch operation on a database. Tables to exclude can be listed as comma-separated list. Applies only with database conversion.", Required = false)]
        public string ExcludeTables { get; set; }

        [Option("SerialQueryFor", DefaultValue = null, HelpText = "Construct slow serial queries instead of bulk ALTER for specified tables. It's intended for use with very large tables where single bulk queries timeouts. Tables can be listed as comma-separated values. Parameter is used only with database conversion, for single table conversion see --SerialQuery.")]
        public string SerialQueryFor { get; set; }

        [Option("SerialQuery", DefaultValue = false, HelpText = "Construct slow serial queries instead of bulk ALTER. It's intended for use with very large tables where single bulk queries timeouts. Parameter applies to all queries during conversion. See also --SerialQueryFor.")]
        public bool SerialQuery { get; set; }

        [Option('f', "ContinueOnError", DefaultValue = false, HelpText = "Continue execution on MySQL errors.", Required = false)]
        public bool ContinueOnError { get; set; }

        [ParserState]
        public IParserState LastParserState { get; set; }

        [HelpOption]
        public string GetUsage() {
            return HelpText.AutoBuild(this, (HelpText c) => HelpText.DefaultParsingErrorsHandler(this, c));
        }
    }
}
