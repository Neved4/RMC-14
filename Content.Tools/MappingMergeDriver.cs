using System;
using System.Linq;

namespace Content.Tools
{
    internal static class MappingMergeDriver
    {
        /// %A: Our file
        /// %O: Origin (common, base) file
        /// %B: Other file
        /// %P: Actual filename of the resulting file
        public static void Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "rotate-tiles")
            {
                Environment.Exit(
                    RotateTilesCommand.Run(args.Skip(1).ToArray()));
            }

            var ours = new Map(args[0]);
            var based = new Map(args[1]); // On what?
            var other = new Map(args[2]);

            var oursGridCount = ours.GridsNode.Children.Count;
            var basedGridCount = based.GridsNode.Children.Count;
            var otherGridCount = other.GridsNode.Children.Count;

            if (oursGridCount != 1 || basedGridCount != 1 ||
                otherGridCount != 1)
            {
                Console.WriteLine(
                    "one or more files had an amount of grids not equal to 1");
                Environment.Exit(1);
            }

            if (!(new Merger(ours, based, other).Merge()))
            {
                Console.WriteLine("unable to merge!");
                Environment.Exit(1);
            }

            ours.Save();
            Environment.Exit(0);
        }
    }
}
