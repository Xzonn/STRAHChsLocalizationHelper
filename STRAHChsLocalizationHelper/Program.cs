using AssetStudio;
using BundleHelper;
using Ionic.Zip;
using LibCPK;
using Newtonsoft.Json;
using SixLabors.ImageSharp.Processing;
using System.Globalization;

namespace Helper
{
    internal class Program
    {
        static void Main()
        {
            Logger.Default = new LogHelper();

            if (!Enum.TryParse(Environment.GetEnvironmentVariable("XZ_PLATFORM") ?? "Switch", out Platform platform))
            {
                throw new ArgumentException("Invalid platform");
            }
            if (!Enum.TryParse(Environment.GetEnvironmentVariable("XZ_GAME") ?? "STRAH", out Game game))
            {
                throw new ArgumentException("Invalid game");
            }

            ExtractFiles(platform, game);
            PatchAsset(platform, game);
            PatchBundle(platform, game);
            PatchPak(platform, game);
            PatchMetadata(platform, game);

            if (platform == Platform.Switch)
            {
                CreateRomfsFolder(game);
            }
            else if (platform == Platform.PS4)
            {
                throw new NotImplementedException("Not implemented");
            }
            else
            {
                throw new ArgumentException("Invalid platform");
            }
        }

        static void ExtractFiles(Platform platform, Game game)
        {
            if (!File.Exists($"original_files/{platform}/Data/level1"))
            {
                Directory.CreateDirectory($"original_files/{platform}/");
                using ZipFile archive = new($"original_files/{platform}.zip")
                {
                    Password = "hogehoge66",
                    Encryption = EncryptionAlgorithm.PkzipWeak,
                    StatusMessageTextWriter = Console.Out
                };
                archive.ExtractAll($"original_files/{platform}/", ExtractExistingFileAction.OverwriteSilently);
            }
        }

        static void PatchAsset(Platform platform, Game game)
        {
            AssetsManager manager = new()
            {
                SpecifyUnityVersion = game switch
                {
                    Game.STRAH => "2020.3.37f1",
                    Game.YCHAND => "2020.3.14f1",
                    _ => throw new ArgumentException("Invalid game")
                }
            };

            manager.LoadFolder($"original_files/{platform}");

            Dictionary<string, string> textTranslations = [];
            if (File.Exists("texts/zh_Hans/Text.json"))
            {
                textTranslations = JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText("texts/zh_Hans/Text.json"))!;
            }

            foreach (var assetsFile in manager.assetsFileList)
            {
                var fileName = Path.GetFileNameWithoutExtension(assetsFile.fileName);
                var assetHelper = new AssetHelper();
                foreach (var @object in assetsFile.Objects)
                {
                    if (@object is MonoBehaviour m_MonoBehaviour
                        && m_MonoBehaviour.m_Script.TryGet(out var m_Script))
                    {
                        assetHelper.ReplaceMonoBehaviour(m_MonoBehaviour, m_Script, textTranslations, game);
                    }
                    else if ((@object is Texture2D m_Texture2D)
                        && File.Exists($"files/images/{m_Texture2D.m_Name}.png"))
                    {
                        assetHelper.ReplaceTexture(m_Texture2D);
                    }
                    else if ((@object is Sprite m_Sprite)
                        && File.Exists($"files/sprites/{m_Sprite.m_Name}.png"))
                    {
                        assetHelper.ReplaceSprite(m_Sprite);
                    }
                    else if ((@object is Font m_Font)
                        && File.Exists($"files/fonts/{m_Font.m_Name}.ttf"))
                    {
                        assetHelper.ReplaceFont(m_Font);
                    }
                }
                foreach (var (m_PathID, image) in assetHelper.ReplacedImages)
                {
                    image.Mutate(_ => _.Flip(FlipMode.Vertical));
                    assetHelper.ReplaceTexture((Texture2D)assetsFile.Objects.Where(_ => _.m_PathID == m_PathID).First());
                    image.Dispose();
                }
                if (assetHelper.ReplacedStreams.Count > 0)
                {
                    string newPath = string.IsNullOrEmpty(assetsFile.originalPath)
                        ? Path.GetRelativePath("original_files", assetsFile.fullName)
                        : assetsFile.fileName;
                    Directory.CreateDirectory(Path.GetDirectoryName($"out/{newPath}")!);
                    assetsFile.SaveAs($"out/{newPath}", assetHelper.ReplacedStreams);
                    foreach (var (m_PathID, stream) in assetHelper.ReplacedStreams)
                    {
                        stream.Dispose();
                    }
                    Console.WriteLine($"Saved: {assetsFile.fileName}");
                }
            }

            var textTranslationsSorted = new Dictionary<string, string>();
            var textKeys = textTranslations.Keys.ToList();
            var comparer = StringComparer.Create(new CultureInfo("ja-JP"), false);
            textKeys.Sort(comparer);
            foreach (var key in textKeys)
            {
                textTranslationsSorted[key] = textTranslations[key];
            }
            File.WriteAllText("texts/zh_Hans/Text.json", JsonConvert.SerializeObject(textTranslationsSorted, Formatting.Indented));
        }

        static void PatchBundle(Platform platform, Game game)
        {
            var files = Directory.GetFiles($"original_files/{platform}", "*.unity3d", SearchOption.AllDirectories);
            foreach (string fileName in files)
            {
                var reader = new BundleHelper.EndianBinaryReader(File.OpenRead(fileName));
                Bundle bundleData = new(reader);

                reader.Close();

                bool changed = false;
                foreach (var file in bundleData.FileList)
                {
                    if (File.Exists($"out/{file.fileName}"))
                    {
                        file.stream = File.OpenRead($"out/{file.fileName}");
                        changed = true;
                    }
                }
                if (!changed)
                {
                    continue;
                }

                Console.WriteLine($"Writing: {fileName}");
                var relativePath = Path.GetRelativePath("original_files", fileName);
                Directory.CreateDirectory(Path.GetDirectoryName($"out/{relativePath}")!);
                var writer = new BundleHelper.EndianBinaryWriter(File.Create($"out/{relativePath}"));
                bundleData.DumpRaw(writer);

                writer.Close();
            }
        }

        static void PatchPak(Platform platform, Game game)
        {
            var writer = new XorWriter();
            foreach (var fileName in Directory.GetFiles("texts/zh_Hans/scrpt.cpk", "*.json"))
            {
                var rawName = Path.GetFileNameWithoutExtension(fileName);
                Console.WriteLine($"Writing: {rawName}");
                writer.Write(fileName, $"out/{rawName}");
            }
            var cpk = new CPK();
            cpk.ReadCPK($"original_files/{platform}/Data/StreamingAssets/scrpt.cpk");
            var replacedFiles = new Dictionary<string, string>();
            var patch = new PatchCPK(cpk, Path.GetFullPath($"original_files/{platform}/Data/StreamingAssets/scrpt.cpk"));
            patch.SetListener(null, Console.WriteLine, null);
            foreach (var file in cpk.fileTable)
            {
                if (File.Exists($"out/{file.FileName}"))
                {
                    replacedFiles[$"/{file.FileName}"] = Path.GetFullPath($"out/{file.FileName}");
                }
            }
            Directory.CreateDirectory($"out/{platform}/Data/StreamingAssets");
            patch.Patch($"out/{platform}/Data/StreamingAssets/scrpt.cpk", true, replacedFiles);
#if !DEBUG
            foreach (var fileName in replacedFiles)
            {
                File.Delete(fileName.Value);
            }
#endif
        }

        struct MetadataItem
        {
            public int position;
            public int length;
            public string text;
        }

        static void PatchMetadata(Platform platform, Game game)
        {
            using var br = new BinaryReader(File.OpenRead($"original_files/{platform}/Data/Managed/Metadata/global-metadata.dat"));

            br.BaseStream.Position = 0x08;
            uint stringLiteralOffset = br.ReadUInt32();
            int stringLiteralSize = br.ReadInt32();
            uint stringLiteralDataOffset = br.ReadUInt32();
            int stringLiteralDataSize = br.ReadInt32();

            var lengthPosition = new Dictionary<long, long>();
            br.BaseStream.Position = stringLiteralOffset;
            for (int i = 0; i < stringLiteralSize; i++)
            {
                long position = br.BaseStream.Position;
                uint length = br.ReadUInt32();
                int dataPosition = br.ReadInt32();
                lengthPosition[dataPosition + stringLiteralDataOffset] = position;
            }

            var metadataList = JsonConvert.DeserializeObject<List<MetadataItem>>(File.ReadAllText("texts/zh_Hans/Metadata.json"))!;
            Directory.CreateDirectory($"out/{platform}/Data/Managed/Metadata");
            using var bw = new BinaryWriter(File.Create($"out/{platform}/Data/Managed/Metadata/global-metadata.dat"));

            br.BaseStream.Position = 0;
            bw.Write(br.ReadBytes((int)br.BaseStream.Length));

            foreach (var item in metadataList)
            {
                bw.BaseStream.Position = item.position;
                byte[] buffer = new byte[item.length];
                byte[] bytes = Encoding.UTF8.GetBytes(item.text);
                if (bytes.Length > item.length)
                {
                    Console.Error.WriteLine($"Text is too long: {item.text}");
                    bytes = bytes.Take(item.length).ToArray();
                }
                bytes.CopyTo(buffer, 0);
                bw.Write(buffer);

                bw.BaseStream.Position = lengthPosition[item.position];
                bw.Write((uint)bytes.Length);
            }
            Console.WriteLine($"Saved: global-metadata.dat");
        }

        static void CreateRomfsFolder(Game game)
        {
            string titleId = (game switch
            {
                Game.STRAH => "01005940182EC000",
                Game.YCHAND => "0100D12014FC2000",
                _ => throw new ArgumentException("Invalid game")
            });
            string romfsDir = $"out/{titleId.ToLower()}/romfs";

            foreach (var fileName in Directory.EnumerateFiles("out/Switch", "*", SearchOption.AllDirectories))
            {
                var relativePath = Path.GetRelativePath("out/Switch", fileName);
                Directory.CreateDirectory(Path.GetDirectoryName($"{romfsDir}/{relativePath}")!);
                File.Move(fileName, $"{romfsDir}/{relativePath}", true);
            }
#if !DEBUG
            Directory.Delete("out/Switch", true);
            foreach (var fileName in Directory.EnumerateFiles("out/", "CAB-*"))
            {
                File.Delete(fileName);
            }
#endif
        }
    }
}
