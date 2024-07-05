using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace _7x_TexAdt_MTXP_Adder
{
    class Program
    {
        static char[] chMver = new[] { 'R', 'E', 'V', 'M' };
        static char[] chMamp = new[] { 'P', 'M', 'A', 'M' };
        static char[] chMtex = new[] { 'X', 'E', 'T', 'M' };
        static char[] chMcnk = new[] { 'K', 'N', 'C', 'M' };
        static char[] chMcly = new[] { 'Y', 'L', 'C', 'M' };
        static char[] chMcal = new[] { 'L', 'A', 'C', 'M' };
        static char[] chMtxp = new[] { 'P', 'X', 'T', 'M' };
        static char[] chMDID = new[] { 'D', 'I', 'D', 'M' };
        static char[] chMHID = new[] { 'D', 'I', 'H', 'M' };

        /*
        * Tex0 Data Add:
        * MCAL needs to be Uncompressed into 4096. 
        * MTXP needs to be generated. 
        * Two config files for MTXP: One for all the textures, one for specific ADTs.
        * TextureName | Scale - Height - heightOffset - groundeffectid
        *
        * 
        * ADT, on average layer for spot > 120, add value to the 2bit flag.
        * Layer0 = bit 1, Layer1 = bit2, Layer3=bit3, Layer4=bit4
        * While processing the texture ADT, process the groundeffects too.
        * When done with tex0, overwrite the relevant values in .adt
        */

        class TextureInfo
        {
            public TextureInfo(byte scale, float heightScale, float heightOffset, uint groundEffect)
            {
                Scale = scale;
                HeightScale = heightScale;
                HeightOffset = heightOffset;
                GroundEffect = groundEffect;
            }

            private byte _scale;
            public byte Scale
            {
                get => _scale;
                set
                {
                    if (value > 15)
                        _scale = 15;
                    else
                        _scale = value;
                }
            }
            public float HeightScale { get; set; }
            public float HeightOffset { get; set; }
            public uint GroundEffect { get; set; }

            public uint GetFlags()
            {
                return (uint)(_scale << 4);
            }
        }

        private static Dictionary<string, TextureInfo> TextureInfo_Global;
        private static Dictionary<string, Dictionary<string, TextureInfo>> TextureInfo_ByADT;
        private static Dictionary<uint, string> Listfile = new Dictionary<uint, string>();
        private static Dictionary<string, uint> ListfileReverse = new Dictionary<string, uint>();

        public static uint GroundEffectCutoffValue = 80;

        static void GetTextureInfo(string adtName, string texture, out TextureInfo texInfo)
        {
            if (texture.EndsWith("_s.blp"))
                texture = texture.Replace("_s.blp", ".blp");
            
            if (TextureInfo_ByADT.ContainsKey(adtName))
            {
                if (TextureInfo_ByADT[adtName].TryGetValue(texture, out texInfo))
                    return;
            }

            if (TextureInfo_Global.TryGetValue(texture, out texInfo))
                return;

            Console.WriteLine("Could not find height texture metadata for texture: " + texture + ", using default values.");

            texInfo = new TextureInfo(1, 0, 1, 0);
        }

        static void GetHeightTextureFDIDForTexture(string diffuseName, out uint heightFDID)
        {
            if (diffuseName.EndsWith("_s.blp"))
                diffuseName = diffuseName.Replace("_s.blp", ".blp");

            var heightName = diffuseName.Replace(".blp", "_h.blp");

            if (!ListfileReverse.TryGetValue(heightName, out heightFDID))
            {
                Console.WriteLine("Could not find height texture for texture: " + diffuseName + ", returning 0.");
                heightFDID = 0;
            }
        }

        static byte[] ExtendMcalValue(byte source)
        {
            byte[] retVal = new byte[2];
            int val1 = source / 16;
            int val2 = source % 16;

            var NewValue1 = (val1 * 255f) / 15f;
            var NewValue2 = (val2 * 255f) / 15f;
            retVal[0] = Convert.ToByte(NewValue1);
            retVal[1] = Convert.ToByte(NewValue2);
            return retVal;
        }

        static void LoadConfig()
        {
            if (!Directory.Exists("config") || !File.Exists("config\\global.cfg"))
            {
                CreateDefaultConfig();
            }

            TextureInfo_ByADT = new Dictionary<string, Dictionary<string, TextureInfo>>();

            StreamReader sr = new StreamReader(File.OpenRead("config\\global.cfg"));
            string sTex = sr.ReadToEnd();
            TextureInfo_Global = JsonConvert.DeserializeObject<Dictionary<string, TextureInfo>>(sTex);
            sr.Close();

            Console.WriteLine("Loaded General Config..");
            var configs = Directory.EnumerateFiles("config", "*.cfg");
            foreach (var cfg in configs)
            {
                sr = new StreamReader(File.OpenRead(cfg));
                sTex = sr.ReadToEnd();
                sr.Close();

                string name = Path.GetFileNameWithoutExtension(cfg).ToLowerInvariant();
                if (name == "global")
                    continue;

                TextureInfo_ByADT[name] = JsonConvert.DeserializeObject<Dictionary<string, TextureInfo>>(sTex);
                Console.WriteLine("Loaded Config for map: " + name);
            }
        }

        static void CreateDefaultConfig()
        {
            var global = new Dictionary<string, TextureInfo>();
            Directory.CreateDirectory("config");
            global.Add("tileset/expansion06/valsharah/7vs_rock_04.blp", new TextureInfo(2, 15.6f, 0.93f, 0));

            StreamWriter sw = new StreamWriter(File.OpenWrite("config\\global.cfg"));

            sw.Write(JValue.Parse(JsonConvert.SerializeObject(global)).ToString(Formatting.Indented));
            sw.Close();

        }

        static void Main(string[] args)
        {
            // Load height texture config
            try
            {
                LoadConfig();
            }
            catch (Exception e)
            {
                Console.ForegroundColor = ConsoleColor.Red;

                Console.WriteLine(e);

                Console.ReadLine();
                Environment.Exit(1);
            }

            // Load Listfile
            Console.WriteLine("Loading listfile");
            if (!File.Exists("listfile.csv"))
                throw new FileNotFoundException("listfile.csv not found, please place it in the same directory as the program.");

            foreach (var line in File.ReadAllLines("listfile.csv"))
            {
                var listfileEntry = line.Split(';');
                if (listfileEntry.Length != 2)
                    continue;

                listfileEntry[1] = listfileEntry[1].ToLowerInvariant();

                if (listfileEntry[1].StartsWith("tileset"))
                {
                    Listfile[uint.Parse(listfileEntry[0])] = listfileEntry[1];
                    ListfileReverse[listfileEntry[1]] = uint.Parse(listfileEntry[0]);
                }
            }

            Console.WriteLine("Listfile loaded, found " + Listfile.Count + " tileset entries.");

            Directory.CreateDirectory("Output");
            var inputList = Directory.EnumerateFiles("Input", "*_tex0.adt");

            ThreadPool.SetMaxThreads(Environment.ProcessorCount, Environment.ProcessorCount);

            var inFiles = inputList as string[] ?? inputList.ToArray();
            int threadCount = inFiles.Count();
            using (var countdownEvent = new CountdownEvent(threadCount))
            {
                foreach (string inFile in inFiles)
                {
                    ThreadPool.QueueUserWorkItem(x =>
                    {
                        ProcessFile(inFile);
                        countdownEvent.Signal();
                    }, inFile);
                }

                countdownEvent.Wait();
            }

            Console.Write("All done!");
            Console.ReadLine();
        }

        private static void ProcessFile(object file)
        {
            string inFile = (string)file;

            using (BinaryReader brTex = new BinaryReader(File.OpenRead(inFile)))
            using (BinaryWriter bwOut = new BinaryWriter(File.OpenWrite("Output\\" + Path.GetFileName(inFile))))
            {
                string curAdtName = Path.GetFileName(inFile).Replace("_tex0.adt", "").ToLowerInvariant();

                List<string> textureList = new List<string>();
                List<uint> textureListFDID = new List<uint>();

                List<byte[]> mcnkGroundEffectMaps = new List<byte[]>();
                int mcnkCount = 0;

                while (brTex.BaseStream.Position < brTex.BaseStream.Length)
                {
                    char[] header = brTex.ReadChars(4);
                    uint size = brTex.ReadUInt32();

                    long nextPos = brTex.BaseStream.Position + size;

                    if (header.SequenceEqual(chMtex)) // MTEX (Legacy)
                    {
                        Console.WriteLine("Warning: Legacy MTEX chunk found, please convert to MDID and rerun the tool, continuing with only writing MHID regardless.");
                        bwOut.Write(header);
                        bwOut.Write(size);

                        if (size > 0)
                        {
                            char[] txts = brTex.ReadChars(Convert.ToInt32(size));

                            string[] textures = new string(txts).Split('\0');

                            // 1 less because of empty string at the end
                            for (int i = 0; i < textures.Length - 1; i++)
                                textureList.Add(textures[i]);

                            bwOut.Write(txts);
                        }
                    }
                    else if (header.SequenceEqual(chMDID)) // MDID
                    {
                        bwOut.Write(header);
                        bwOut.Write(size);

                        if (size > 0)
                        {
                            var prevPos = brTex.BaseStream.Position;

                            var textureCount = size / 4;
                            for (int i = 0; i < textureCount; i++)
                                textureListFDID.Add(brTex.ReadUInt32());

                            brTex.BaseStream.Position = prevPos;
                            var txts = brTex.ReadBytes(Convert.ToInt32(size));
                            bwOut.Write(txts);
                        }
                    }
                    else if (header.SequenceEqual(chMHID)) // MHID 
                    {
                        bwOut.Write(header);
                        bwOut.Write(size);

                        if (size > 0)
                        {
                            brTex.BaseStream.Position += size;
                            for (int i = 0; i < textureListFDID.Count; i++)
                            {
                                uint heightFDID = 0;

                                if(Listfile.TryGetValue(textureListFDID[i], out var textureFilename))
                                {
                                    GetHeightTextureFDIDForTexture(textureFilename, out heightFDID);
                                }
                                else
                                {
                                    Console.WriteLine("Could not find height texture for texture FDID: " + textureListFDID[i] + ", using 0.");
                                }

                                bwOut.Write(heightFDID);
                            }
                        }
                    }
                    else if (header.SequenceEqual(chMcnk)) // MCNK
                    {
                        //Console.WriteLine("Mcnk..");
                        bwOut.Write(header);
                        bwOut.Write(size);

                        // Update length later
                        long mcnkSizePos = bwOut.BaseStream.Position - 4;

                        while (brTex.BaseStream.Position < nextPos)
                        {
                            char[] subHeader = brTex.ReadChars(4);
                            uint subSize = brTex.ReadUInt32();
                            long nextSubPos = brTex.BaseStream.Position + subSize;

                            if (subHeader.SequenceEqual(chMcly))
                            {
                                bwOut.Write(subHeader);
                                bwOut.Write(subSize);

                                while (brTex.BaseStream.Position < nextSubPos)
                                {
                                    uint textureId = brTex.ReadUInt32();
                                    bwOut.Write(textureId);
                                    bwOut.Write(brTex.ReadUInt32());

                                    // Double in size each, so  0 -> 0, 2048 -> 4096, 4096 -> 8192
                                    uint mclyPos = brTex.ReadUInt32();
                                    bwOut.Write(mclyPos); //* 2);

                                    var groundEffect = brTex.ReadUInt32(); // Skip and replace groundeffect
                                    bwOut.Write(groundEffect);
                                }
                            }
                            else if (subHeader.SequenceEqual(chMcal))
                            {
                                bwOut.Write(subHeader);
                                bwOut.Write(subSize);

                                byte[] layerData = brTex.ReadBytes(Convert.ToInt32(subSize));

                                bwOut.Write(layerData);
                            }
                            else
                            {
                                if (subSize > 0)
                                    brTex.ReadBytes(Convert.ToInt32(subSize));
                            }
                        }

                        long curPos = bwOut.BaseStream.Position;
                        bwOut.BaseStream.Position = mcnkSizePos;
                        bwOut.Write(Convert.ToUInt32(curPos - mcnkSizePos) - 4);
                        bwOut.BaseStream.Position = curPos;

                        mcnkCount++;
                    }
                    else
                    {
                        Console.WriteLine("Writing existing " + new string(header) + " chunk..");
                        bwOut.Write(header);
                        bwOut.Write(size);
                        if (brTex.BaseStream.Position < nextPos)
                        {
                            bwOut.Write(brTex.ReadBytes(Convert.ToInt32(size)));
                        }
                    }
                }

                brTex.Close();

                //Console.WriteLine("Writing MTXP...");
                // New MTXP is written
                bwOut.Write(chMtxp);

                // Will write when done 
                long mtxpLenPos = bwOut.BaseStream.Position;
                bwOut.Write((uint)64);

                if(textureListFDID.Count > 0)
                {
                    // Prefer FDID method
                    for (int i = 0; i < textureListFDID.Count; i++)
                    {
                        TextureInfo txInfo = new TextureInfo(1, 0, 1, 0);

                        if (Listfile.TryGetValue(textureListFDID[i], out var textureFilename))
                        {
                            GetTextureInfo(curAdtName, textureFilename, out txInfo);
                        }
                        else
                        {
                            Console.WriteLine("Could not find height texture for texture FDID: " + textureListFDID[i] + ", using 0.");
                        }

                        bwOut.Write(txInfo.GetFlags());
                        bwOut.Write(txInfo.HeightScale);
                        bwOut.Write(txInfo.HeightOffset);
                        bwOut.Write((uint)0); // Padding?
                    }
                }
                else
                {
                    // Otherwise fallback to filenames
                    for (int i = 0; i < textureList.Count; i++)
                    {
                        GetTextureInfo(curAdtName, textureList[i], out TextureInfo txInfo);

                        bwOut.Write(txInfo.GetFlags());
                        bwOut.Write(txInfo.HeightScale);
                        bwOut.Write(txInfo.HeightOffset);
                        bwOut.Write((uint)0); // Padding?
                    }
                }
                
                long endPos = bwOut.BaseStream.Position;
                bwOut.BaseStream.Position = mtxpLenPos;
                bwOut.Write(Convert.ToUInt32(endPos - mtxpLenPos) - 4);
                bwOut.Close();

                Console.WriteLine(curAdtName + " Done!");
            }
        }
    }
}
