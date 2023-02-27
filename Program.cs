using AssetStudio;
using LibCPK;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BundleHelper;
using System.Globalization;
using Ionic.Zip;

namespace Helper
{
    internal class Program
    {
        static void Main(string[] args)
        {
            if (args.Contains("4"))
            {
                ExtractFiles();
            }
            if (args.Contains("0"))
            {
                PatchAsset();
            }
            if (args.Contains("1"))
            {
                PatchBundle();
            }
            if (args.Contains("2"))
            {
                PatchPak();
            }
            if (args.Contains("3"))
            {
                CreateRomfsFolder();
            }
        }

        static void ExtractFiles()
        {
            using (ZipFile archive = new ZipFile("files/files.zip"))
            {
                archive.Password = "hogehoge66";
                archive.Encryption = EncryptionAlgorithm.PkzipWeak;
                archive.StatusMessageTextWriter = System.Console.Out;
                archive.ExtractAll("files", ExtractExistingFileAction.OverwriteSilently);
            }
        }

        static void PatchAsset()
        {
            Logger.Default = new LogHelper();

            string[] FILE_NAMES = new string[]
            {
                "level1",
                "vridge.unity3d",

                "adv2.unity3d",
                "dataselect.unity3d",
                "omkalb.unity3d",
                "title.unity3d",
            };
            string[] CLASS_FOR_EXPORT = new string[]
            {
                "AppGameDataTipsData",
                "FlowChartData",
            };

            AssetsManager manager = new AssetsManager
            {
                SpecifyUnityVersion = "2020.3.37f1"
            };
            AssemblyLoader assemblyLoader = new AssemblyLoader();
            assemblyLoader.Load("files/DummyDll");

            manager.LoadFiles(FILE_NAMES.Select(x => $"files/{x}").ToArray());
            if (!Directory.Exists("json")) { Directory.CreateDirectory("json"); }
            if (!Directory.Exists("out")) { Directory.CreateDirectory("out"); }

            Dictionary<string, string> textTranslations = new Dictionary<string, string>();
            if (File.Exists("translated/Text.json"))
            {
                textTranslations = JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText("translated/Text.json"));
            }

            foreach (var assetsFile in manager.assetsFileList)
            {
                var replaceStreams = new Dictionary<long, Stream> { };
                foreach (var @object in assetsFile.Objects)
                {
                    if (!(@object is MonoBehaviour m_MonoBehaviour) || !m_MonoBehaviour.m_Script.TryGet(out var m_Script)) { continue; }
                    var m_ClassName = m_Script.m_ClassName;
                    if (CLASS_FOR_EXPORT.Contains(m_ClassName))
                    {
                        var m_Type = m_MonoBehaviour.serializedType?.m_Type;
                        if (m_Type == null)
                        {
                            m_Type = m_MonoBehaviour.ConvertToTypeTree(assemblyLoader);
                        }

                        if (!File.Exists($"translated/{m_ClassName}.json"))
                        {
                            var type = m_MonoBehaviour.ToType(m_Type);
                            string json = JsonConvert.SerializeObject(type, Formatting.Indented);
                            File.WriteAllText($"translated/{m_Script.m_ClassName}.json", json);
                        }
                        else
                        {
                            string json = File.ReadAllText($"translated/{m_Script.m_ClassName}.json");
                            var jObject = JsonConvert.DeserializeObject<JObject>(json);
                            var type = JsonHelper.ReadType(m_Type, jObject);
                            MemoryStream memoryStream = new MemoryStream();
                            BinaryWriter bw = new BinaryWriter(memoryStream);
                            TypeTreeHelper.WriteType(type, m_Type, bw);
                            replaceStreams[m_MonoBehaviour.m_PathID] = memoryStream;
                        }
                    }
                    else if (m_ClassName == "Text")
                    {
                        var m_Type = m_MonoBehaviour.serializedType?.m_Type;
                        if (m_Type == null)
                        {
                            m_Type = m_MonoBehaviour.ConvertToTypeTree(assemblyLoader);
                        }
                        var type = m_MonoBehaviour.ToType(m_Type);
                        string text = (string)type["m_Text"];
                        if (textTranslations.TryGetValue(text, out string translation))
                        {
                            if (translation != text)
                            {
                                type["m_Text"] = translation;
                                MemoryStream memoryStream = new MemoryStream();
                                BinaryWriter bw = new BinaryWriter(memoryStream);
                                TypeTreeHelper.WriteType(type, m_Type, bw);
                                replaceStreams[m_MonoBehaviour.m_PathID] = memoryStream;
                            }
                        }
                        else
                        {
                            textTranslations[text] = text;
                        }
                    }
                }
                if (replaceStreams.Count > 0)
                {
                    assetsFile.SaveAs($"out/{assetsFile.fileName}", replaceStreams);
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
            File.WriteAllText("translated/Text.json", JsonConvert.SerializeObject(textTranslationsSorted, Formatting.Indented));
        }

        static void PatchBundle()
        {
            string[] FILE_NAMES = new string[]
            {
                "vridge.unity3d",

                "adv2.unity3d",
                "dataselect.unity3d",
                "omkalb.unity3d",
                "title.unity3d",
            };

            foreach (string fileName in FILE_NAMES)
            {
                var reader = new BundleHelper.EndianBinaryReader(File.OpenRead($"files/{fileName}"));
                Bundle bundleData = new Bundle(reader);

                reader.Close();

                bool changed = false;
                foreach (var file in bundleData.FileList)
                {
                    if (File.Exists($"out/{file.fileName}"))
                    {
                        Console.WriteLine(file.fileName);
                        file.stream = File.OpenRead($"out/{file.fileName}");
                        changed = true;
                    }
                }
                if (!changed)
                {
                    File.Copy($"files/{fileName}", $"out/{fileName}", true);
                    continue;
                }

                Console.WriteLine($"Writing: {fileName}");
                var writer = new BundleHelper.EndianBinaryWriter(File.Create($"out/{fileName}"));
                bundleData.DumpRaw(writer);

                writer.Close();
            }
        }

        static void PatchPak()
        {
            var writer = new XorWriter();
            foreach (var fileName in Directory.GetFiles("translated/scrpt.cpk", "*.json"))
            {
                var rawName = Path.GetFileNameWithoutExtension(fileName);
                Console.WriteLine($"Writing: {rawName}");
                writer.Write(fileName, Path.Combine("out", rawName));
            }
            var cpk = new CPK();
            cpk.ReadCPK("files/scrpt.cpk");
            var batch_file_list = new Dictionary<string, string>();
            var patch = new PatchCPK(cpk, Path.GetFullPath("files/scrpt.cpk"));
            patch.SetListener(null, Console.WriteLine, null);
            foreach (var file in cpk.fileTable)
            {
                if (File.Exists($"out/{file.FileName}"))
                {
                    batch_file_list[$"/{file.FileName}"] = Path.GetFullPath($"out/{file.FileName}");
                }
            }
            patch.Patch("out/scrpt.cpk", true, batch_file_list);
        }

        static void CreateRomfsFolder()
        {
            Directory.CreateDirectory("out/romfs/Data/StreamingAssets/Switch/AssetBundles/data/");
            File.Copy("out/level1",         "out/romfs/Data/level1",                                                  true);
            File.Copy("out/scrpt.cpk",      "out/romfs/Data/StreamingAssets/scrpt.cpk",                               true);
            File.Copy("out/vridge.unity3d", "out/romfs/Data/StreamingAssets/Switch/AssetBundles/data/vridge.unity3d", true);
            Directory.CreateDirectory("out/romfs/Data/StreamingAssets/Switch/AssetBundles/mgr/");
            File.Copy("out/adv2.unity3d",       "out/romfs/Data/StreamingAssets/Switch/AssetBundles/mgr/adv2.unity3d",       true);
            File.Copy("out/dataselect.unity3d", "out/romfs/Data/StreamingAssets/Switch/AssetBundles/mgr/dataselect.unity3d", true);
            File.Copy("out/omkalb.unity3d",     "out/romfs/Data/StreamingAssets/Switch/AssetBundles/mgr/omkalb.unity3d",     true);
            File.Copy("out/title.unity3d",      "out/romfs/Data/StreamingAssets/Switch/AssetBundles/mgr/title.unity3d",      true);
        }
    }
}