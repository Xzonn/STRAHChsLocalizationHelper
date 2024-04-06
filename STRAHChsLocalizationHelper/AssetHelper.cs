using AssetStudio;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System.Collections.Specialized;
using System.Diagnostics;
using Color = SixLabors.ImageSharp.Color;

namespace Helper
{
    internal class AssetHelper
    {
        static readonly string[] CLASS_FOR_EXPORT =
            [
                "AppGameDataTipsData",
                "FlowChartData",
                "TextFlyMoveData",

                "Text",
            ];
        static readonly string[] SPRITE_BLACKLIST =
           [
               "Title_Copyright",
                "Title_logo",
            ];
        public readonly Dictionary<long, Stream> ReplacedStreams = [];
        public readonly Dictionary<long, Image<Bgra32>> ReplacedImages = [];

        public bool ReplaceMonoBehaviour(MonoBehaviour m_MonoBehaviour, MonoScript m_Script, Dictionary<string, string> textTranslations)
        {
            var m_ClassName = m_Script.m_ClassName;
            if (!CLASS_FOR_EXPORT.Contains(m_ClassName)) { return false; }
            var m_Type = m_MonoBehaviour.serializedType?.m_Type;
            if (m_Type == null)
            {
                using var fs = File.OpenRead($"files/TypeTree/{m_ClassName}.bin");
                m_Type = TypeTreeHelper.LoadTypeTree(new BinaryReader(fs));
            }
            var type = m_MonoBehaviour.ToType(m_Type);

            if (m_ClassName == "Text")
            {
                string text = (string)type["m_Text"]!;
                if (textTranslations.TryGetValue(text, out var translation))
                {
                    if (translation != text)
                    {
                        type["m_Text"] = translation;
                        ReplaceWith(m_MonoBehaviour.m_PathID, type, m_Type);
                    }
                }
                else
                {
                    textTranslations[text] = text;
                    return false;
                }
            }
            else
            {
                if (!File.Exists($"texts/zh_Hans/{m_ClassName}.json"))
                {
                    string json = JsonConvert.SerializeObject(type, Formatting.Indented);
                    File.WriteAllText($"texts/zh_Hans/{m_Script.m_ClassName}.json", json);
                    Console.WriteLine($"Extracted (MonoBehaviour): {m_MonoBehaviour.assetsFile.fileName}/{m_ClassName}");
                    return false;
                }
                else
                {
                    string json = File.ReadAllText($"texts/zh_Hans/{m_Script.m_ClassName}.json");
                    var jObject = JsonConvert.DeserializeObject<JObject>(json);
                    type = JsonHelper.ReadType(m_Type, jObject);
                    ReplaceWith(m_MonoBehaviour.m_PathID, type, m_Type);
                }
            }

            Console.WriteLine($"Replaced (MonoBehaviour): {m_MonoBehaviour.assetsFile.fileName}/{m_ClassName} ({m_MonoBehaviour.m_PathID})");
            return true;
        }

        public bool ReplaceTexture(Texture2D m_Texture2D)
        {
            var m_Type = m_Texture2D.serializedType?.m_Type;
            if (m_Type == null)
            {
                using var fs = File.OpenRead($"files/TypeTree/Texture2D.bin");
                m_Type = TypeTreeHelper.LoadTypeTree(new BinaryReader(fs));
            }
            var type = m_Texture2D.ToType(m_Type);

            if (m_Texture2D.assetsFile.m_TargetPlatform == BuildTarget.Switch && (bool)type["m_IsPreProcessed"]!)
            {

                type["m_IsPreProcessed"] = false;
                ((List<object>)type["m_PlatformBlob"]!).Clear();
            }

            int width = (int)type["m_Width"]!;
            int height = (int)type["m_Height"]!;
            byte[] rawData = (byte[])type["image data"]!;
            if (rawData.Length == 0)
            {
                var m_StreamData = (OrderedDictionary)type["m_StreamData"]!;
                m_StreamData["path"] = "";
                m_StreamData["offset"] = (ulong)0;
                m_StreamData["size"] = (uint)0;
            }

            type["m_TextureFormat"] = (int)TextureFormat.BGRA32;

            ReplacedImages.TryGetValue(m_Texture2D.m_PathID, out var bitmap);
            bitmap ??= Image.Load<Bgra32>(File.ReadAllBytes($"files/images/{m_Texture2D.m_Name}.png"));
            bitmap.Mutate(_ => _.Flip(FlipMode.Vertical));
            rawData = new byte[width * height * 4];
            bitmap.CopyPixelDataTo(rawData);
            bitmap.Dispose();

            type["image data"] = rawData;
            type["m_CompleteImageSize"] = (uint)rawData.Length;
            ReplaceWith(m_Texture2D.m_PathID, type, m_Type);

            Console.WriteLine($"Replaced (Texture2D): {m_Texture2D.assetsFile.fileName}/{m_Texture2D.m_Name} ({m_Texture2D.m_PathID})");
            return true;
        }

        public bool ReplaceSprite(Sprite m_Sprite)
        {
            if (SPRITE_BLACKLIST.Contains(m_Sprite.m_Name)) { return false; }
            var m_Type = m_Sprite.serializedType?.m_Type;
            if (m_Type == null)
            {
                using var fs = File.OpenRead($"files/TypeTree/Sprite.bin");
                m_Type = TypeTreeHelper.LoadTypeTree(new BinaryReader(fs));
            }
            var type = m_Sprite.ToType(m_Type);

            Texture2D? m_Texture2D = null;
            Rectf? textureRect = null;
            float downscaleMultiplier = 1f;
            if (m_Sprite.m_SpriteAtlas != null && m_Sprite.m_SpriteAtlas.TryGet(out var m_SpriteAtlas))
            {
                if (m_SpriteAtlas.m_RenderDataMap.TryGetValue(m_Sprite.m_RenderDataKey, out var spriteAtlasData)
                    && spriteAtlasData.texture.TryGet(out m_Texture2D))
                {
                    textureRect = spriteAtlasData.textureRect;
                    downscaleMultiplier = spriteAtlasData.downscaleMultiplier;
                }
            }
            else if (m_Sprite.m_RD.texture.TryGet(out m_Texture2D))
            {
                textureRect = m_Sprite.m_RD.textureRect;
                downscaleMultiplier = m_Sprite.m_RD.downscaleMultiplier;
            }
            Debug.Assert(downscaleMultiplier == 1f);
            if (m_Texture2D == null) { return false; }
            ReplacedImages.TryGetValue(m_Texture2D.m_PathID, out var textureImage);
            textureImage ??= m_Texture2D.ConvertToImage(false);
            if (textureImage == null) { return false; }
            ReplacedImages[m_Texture2D.m_PathID] = textureImage;

            using var reader = File.OpenRead($"files/sprites/{m_Sprite.m_Name}.png");
            using var spriteImage = Image.Load(reader);
            spriteImage.Mutate(x => x.Flip(FlipMode.Vertical));

            var rectX = (int)Math.Floor(textureRect!.x);
            var rectY = (int)Math.Floor(textureRect.y);
            var rectRight = (int)Math.Ceiling(textureRect.x + textureRect.width);
            var rectBottom = (int)Math.Ceiling(textureRect.y + textureRect.height);
            rectRight = Math.Min(rectRight, textureImage.Width);
            rectBottom = Math.Min(rectBottom, textureImage.Height);
            var rect = new Rectangle(rectX, rectY, rectRight - rectX, rectBottom - rectY);
            textureImage.Mutate(x => x.Clear(Color.Transparent, rect));
            textureImage.Mutate(x => x.DrawImage(spriteImage, new Point(rectX, rectY), 1f));

            var newWidth = textureRect.width - (rectRight - rectX) + spriteImage.Width;
            var newHeight = textureRect.height - (rectBottom - rectY) + spriteImage.Height;
            var textureRectDict = (OrderedDictionary)((OrderedDictionary)type["m_RD"]!)["textureRect"]!;
            textureRectDict["width"] = newWidth;
            textureRectDict["height"] = newHeight;

            ReplaceWith(m_Sprite.m_PathID, type, m_Type);

            Console.WriteLine($"Replaced (Sprite): {m_Sprite.assetsFile.fileName}/{m_Sprite.m_Name} ({m_Sprite.m_PathID})");
            return true;
        }

        public bool ReplaceFont(Font m_Font)
        {
            TypeTree m_Type;
            using (var fs = File.OpenRead("files/TypeTree/Font.bin"))
            {
                m_Type = TypeTreeHelper.LoadTypeTree(new BinaryReader(fs));
            }
            var type = m_Font.ToType(m_Type);
            type["m_FontData"] = File.ReadAllBytes($"files/fonts/{m_Font.m_Name}.ttf").Cast<object>().ToList();

            ReplaceWith(m_Font.m_PathID, type, m_Type);

            Console.WriteLine($"Replaced (Font): {m_Font.assetsFile.fileName}/{m_Font.m_Name}");
            return true;
        }

        private void ReplaceWith(long m_PathID, OrderedDictionary type, TypeTree m_Type)
        {
            MemoryStream memoryStream = new();
            BinaryWriter bw = new(memoryStream);
            TypeTreeHelper.WriteType(type, m_Type, bw);
            ReplacedStreams[m_PathID] = memoryStream;
        }
    }
}
