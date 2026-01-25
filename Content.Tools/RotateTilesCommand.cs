using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Content.Tools
{
    internal static class RotateTilesCommand
    {
        private readonly record struct RotatedTileMapping(
            string BaseId,
            byte Rotation);

        private static readonly Dictionary<string, RotatedTileMapping>
            TileMappings = BuildTileMappings();

        public static int Run(string[] args)
        {
            if (args.Length == 0)
            {
                Console.WriteLine(
                    "usage: rotate-tiles <map-or-dir> [more paths]");
                return 1;
            }

            var files = CollectMapFiles(args);
            if (files.Count == 0)
            {
                Console.WriteLine("no map files found");
                return 1;
            }

            var updated = 0;
            foreach (var file in files)
            {
                if (ProcessFile(file))
                    updated++;
            }

            Console.WriteLine($"updated {updated} map file(s)");
            return 0;
        }

        private static List<string> CollectMapFiles(IEnumerable<string> args)
        {
            var files = new List<string>();
            foreach (var path in args)
            {
                if (Directory.Exists(path))
                {
                    files.AddRange(
                        Directory.EnumerateFiles(
                            path,
                            "*.yml",
                            SearchOption.AllDirectories));
                    continue;
                }

                if (File.Exists(path))
                {
                    files.Add(path);
                }
            }

            return files;
        }

        private static bool ProcessFile(string path)
        {
            using var reader = new StreamReader(path);
            var stream = new YamlStream();
            stream.Load(reader);

            var root = (YamlMappingNode) stream.Documents[0].RootNode;
            if (!TryGetMapping(root, "meta", out var metaNode))
                return false;

            if (!TryGetScalar(metaNode, "format", out var formatValue) ||
                formatValue != "7")
            {
                return false;
            }

            if (!TryGetMapping(root, "tilemap", out var tilemapNode))
                return false;

            var tileMapById = ReadTileMap(tilemapNode);
            var rotationByTileId = BuildRotationMap(tileMapById);
            if (rotationByTileId.Count == 0)
                return false;

            var updated = false;
            foreach (var (tileId, mapping) in rotationByTileId)
            {
                var key = new YamlScalarNode(
                    tileId.ToString(CultureInfo.InvariantCulture));
                tilemapNode.Children[key] = new YamlScalarNode(mapping.BaseId);
                updated = true;
            }

            if (!TryGetSequence(root, "entities", out var entitiesNode))
                return updated;

            foreach (var mapNode in entitiesNode.Children.OfType<
                YamlMappingNode>())
            {
                if (!TryGetSequence(mapNode, "entities", out var mapEntities))
                    continue;

                foreach (var entity in mapEntities.Children.OfType<
                    YamlMappingNode>())
                {
                    if (!TryGetSequence(entity, "components", out var comps))
                        continue;

                    foreach (var comp in comps.Children.OfType<
                        YamlMappingNode>())
                    {
                        if (!TryGetScalar(comp, "type", out var type) ||
                            type != "MapGrid")
                        {
                            continue;
                        }

                        if (!TryGetMapping(comp, "chunks", out var chunks))
                            continue;

                        if (UpdateChunks(chunks, rotationByTileId))
                            updated = true;
                    }
                }
            }

            if (!updated)
                return false;

            using var writer = new StreamWriter(path);
            var document = new YamlDocument(root);
            var outStream = new YamlStream(document);
            var emitter = new Emitter(writer);
            var fixer = new TypeTagPreserver(emitter);
            outStream.Save(fixer, false);
            writer.Flush();

            return true;
        }

        private static Dictionary<int, string> ReadTileMap(
            YamlMappingNode tilemapNode)
        {
            var result = new Dictionary<int, string>();
            foreach (var (key, value) in tilemapNode.Children)
            {
                if (key is not YamlScalarNode keyScalar ||
                    value is not YamlScalarNode valueScalar ||
                    keyScalar.Value == null ||
                    valueScalar.Value == null)
                {
                    continue;
                }

                var id = int.Parse(
                    keyScalar.Value,
                    CultureInfo.InvariantCulture);
                result[id] = valueScalar.Value;
            }

            return result;
        }

        private static Dictionary<int, RotatedTileMapping> BuildRotationMap(
            Dictionary<int, string> tileMapById)
        {
            var result = new Dictionary<int, RotatedTileMapping>();
            foreach (var (tileId, tileName) in tileMapById)
            {
                if (!TileMappings.TryGetValue(tileName, out var mapping))
                    continue;

                result[tileId] = mapping;
            }

            return result;
        }

        private static bool UpdateChunks(
            YamlMappingNode chunksNode,
            Dictionary<int, RotatedTileMapping> rotationByTileId)
        {
            var updated = false;
            foreach (var chunkEntry in chunksNode.Children.Values)
            {
                if (chunkEntry is not YamlMappingNode chunk)
                    continue;

                if (!TryGetScalar(chunk, "version", out var versionValue) ||
                    versionValue != "7")
                {
                    continue;
                }

                if (!TryGetScalar(chunk, "tiles", out var tilesValue))
                    continue;

                var bytes = Convert.FromBase64String(tilesValue);
                if (bytes.Length % 7 != 0)
                    continue;

                var outBytes = new byte[bytes.Length];
                using var input = new MemoryStream(bytes);
                using var reader = new BinaryReader(input);
                using var output = new MemoryStream(outBytes);
                using var writer = new BinaryWriter(output);

                while (reader.BaseStream.Position < bytes.Length)
                {
                    var tileId = reader.ReadInt32();
                    var flags = reader.ReadByte();
                    var variant = reader.ReadByte();
                    var rotation = reader.ReadByte();

                    if (rotationByTileId.TryGetValue(tileId, out var map))
                    {
                        rotation = CombineRotation(rotation, map.Rotation);
                        updated = true;
                    }

                    writer.Write(tileId);
                    writer.Write(flags);
                    writer.Write(variant);
                    writer.Write(rotation);
                }

                chunk.Children[new YamlScalarNode("tiles")] =
                    new YamlScalarNode(Convert.ToBase64String(outBytes));
            }

            return updated;
        }

        private static byte CombineRotation(byte current, byte add)
        {
            var mirror = (byte) (current & 0x4);
            var rotation = (byte) (current & 0x3);
            rotation = (byte) ((rotation + add) & 0x3);
            return (byte) (mirror | rotation);
        }

        private static Dictionary<string, RotatedTileMapping>
            BuildTileMappings()
        {
            var mappings = new Dictionary<string, RotatedTileMapping>(
                StringComparer.Ordinal);

            AddCardinalGroup(
                mappings,
                "CMFloorCargoArrowDown",
                Direction.South,
                ("CMFloorCargoArrowDown", Direction.South),
                ("CMFloorCargoArrowUp", Direction.North),
                ("CMFloorCargoArrowRight", Direction.East),
                ("CMFloorCargoArrowLeft", Direction.West));

            AddCardinalGroup(
                mappings,
                "CMFloorCorsatArrowSouth",
                Direction.South,
                ("CMFloorCorsatArrowSouth", Direction.South),
                ("CMFloorCorsatArrowNorth", Direction.North),
                ("CMFloorCorsatArrowEast", Direction.East),
                ("CMFloorCorsatArrowWest", Direction.West));

            AddCardinalGroup(
                mappings,
                "RMCFloorAINoBuildArrow",
                Direction.South,
                ("RMCFloorAINoBuildArrow", Direction.South),
                ("RMCFloorAINoBuildArrowNorth", Direction.North),
                ("RMCFloorAINoBuildArrowEast", Direction.East),
                ("RMCFloorAINoBuildArrowWest", Direction.West));

            AddCardinalGroup(
                mappings,
                "CMFloorOuterHullSouth",
                Direction.South,
                ("CMFloorOuterHullSouth", Direction.South),
                ("CMFloorOuterHullNorth", Direction.North),
                ("CMFloorOuterHullEast", Direction.East),
                ("CMFloorOuterHullWest", Direction.West));

            AddDiagonalGroup(
                mappings,
                "CMFloorOuterHullSouthEast",
                ("CMFloorOuterHullSouthEast", 0),
                ("CMFloorOuterHullNorthEast", 1),
                ("CMFloorOuterHullNorthWest", 2),
                ("CMFloorOuterHullSouthWest", 3));

            AddCardinalGroup(
                mappings,
                "CMFloorSteelPrisonRampNorth",
                Direction.North,
                ("CMFloorSteelPrisonRampNorth", Direction.North),
                ("CMFloorSteelPrisonRampSouth", Direction.South),
                ("CMFloorSteelPrisonRampEast", Direction.East),
                ("CMFloorSteelPrisonRampWest", Direction.West));

            AddCardinalGroup(
                mappings,
                "RMCFloorHybrisaRampNorth",
                Direction.North,
                ("RMCFloorHybrisaRampNorth", Direction.North),
                ("RMCFloorHybrisaRampSouth", Direction.South),
                ("RMCFloorHybrisaRampEast", Direction.East),
                ("RMCFloorHybrisaRampWest", Direction.West));

            AddCardinalGroup(
                mappings,
                "RMCFloorHybrisaStripeRedNorth",
                Direction.North,
                ("RMCFloorHybrisaStripeRedNorth", Direction.North),
                ("RMCFloorHybrisaStripeRedSouth", Direction.South),
                ("RMCFloorHybrisaStripeRedEast", Direction.East),
                ("RMCFloorHybrisaStripeRedWest", Direction.West));

            return mappings;
        }

        private static void AddCardinalGroup(
            Dictionary<string, RotatedTileMapping> mappings,
            string baseId,
            Direction baseDirection,
            params (string Id, Direction Direction)[] variants)
        {
            var baseRotation = Tile.DirectionToByte(baseDirection, true);
            foreach (var (id, direction) in variants)
            {
                var target = Tile.DirectionToByte(direction, true);
                var rotation = (byte) ((target - baseRotation + 4) & 0x3);
                mappings[id] = new RotatedTileMapping(baseId, rotation);
            }
        }

        private static void AddDiagonalGroup(
            Dictionary<string, RotatedTileMapping> mappings,
            string baseId,
            params (string Id, byte Rotation)[] variants)
        {
            foreach (var (id, rotation) in variants)
                mappings[id] = new RotatedTileMapping(baseId, rotation);
        }

        private static bool TryGetMapping(
            YamlMappingNode node,
            string key,
            out YamlMappingNode mapping)
        {
            mapping = null!;
            if (!node.Children.TryGetValue(new YamlScalarNode(key),
                out var child) || child is not YamlMappingNode map)
            {
                return false;
            }

            mapping = map;
            return true;
        }

        private static bool TryGetSequence(
            YamlMappingNode node,
            string key,
            out YamlSequenceNode sequence)
        {
            sequence = null!;
            if (!node.Children.TryGetValue(new YamlScalarNode(key),
                out var child) || child is not YamlSequenceNode seq)
            {
                return false;
            }

            sequence = seq;
            return true;
        }

        private static bool TryGetScalar(
            YamlMappingNode node,
            string key,
            out string value)
        {
            value = string.Empty;
            if (!node.Children.TryGetValue(new YamlScalarNode(key),
                out var child) || child is not YamlScalarNode scalar ||
                scalar.Value == null)
            {
                return false;
            }

            value = scalar.Value;
            return true;
        }
    }
}
