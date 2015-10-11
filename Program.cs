using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommandLine;

namespace sqlcsconv {
    class Program {
        static void Main(string[] args) {
            var opts = new Options();
            if (!Parser.Default.ParseArguments(args, opts)) {
                Environment.Exit(1);
            }
        }
    }
}
