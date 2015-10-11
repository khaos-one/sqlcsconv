using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommandLine;
using CommandLine.Text;

namespace sqlcsconv {
    class Options {
        public string DbHost { get; set; }
        public string DbUser { get; set; }
        public string DbPassword { get; set; }
        public string Target { get; set; }
        
        public string SourceEncoding { get; set; }
        public string DestEncoding { get; set; }
        
        public bool GenerateScript { get; set; }

        public bool Verbose { get; set; }


        [ParserState]
        public IParserState LastParserState { get; set; }

        [HelpOption]
        public string GetUsage() {
            return HelpText.AutoBuild(this, (HelpText c) => HelpText.DefaultParsingErrorsHandler(this, c));
        }
    }
}
