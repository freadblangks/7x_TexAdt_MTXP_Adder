using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

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

        public static uint GroundEffectCutoffValue = 80;

        static void GetTextureInfo(string adtName, string texture, out TextureInfo texInfo)
        {
            if (TextureInfo_ByADT.ContainsKey(adtName))
            {
                if (TextureInfo_ByADT[adtName].ContainsKey(texture))
                {
                    texInfo = TextureInfo_ByADT[adtName][texture];
                    return;
                }
            }

            if (TextureInfo_Global.ContainsKey(texture))
            {
                texInfo = TextureInfo_Global[texture];
                return;
            }

            texInfo = new TextureInfo(1, 0, 1, 0);
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
            Console.Beep();
            Console.ReadLine();
        }

        private static void ProcessFile(object file)
        {
            string inFile = (string) file;

            using (BinaryReader brTex = new BinaryReader(File.OpenRead(inFile)))
            using (BinaryWriter bwOut = new BinaryWriter(File.OpenWrite("Output\\" + Path.GetFileName(inFile))))
            {
                string curAdtName = Path.GetFileName(inFile).Replace("_tex0.adt", "").ToLowerInvariant();

                List<string> textureList = new List<string>();

                List<byte[]> mcnkGroundEffectMaps = new List<byte[]>();
                int mcnkCount = 0;

                while (brTex.BaseStream.Position < brTex.BaseStream.Length)
                {
                    char[] header = brTex.ReadChars(4);
                    uint size = brTex.ReadUInt32();

                    long nextPos = brTex.BaseStream.Position + size;

                    if (header.SequenceEqual(chMtex))
                    {
                        //Console.WriteLine("Mtex..");
                        bwOut.Write(header);
                        bwOut.Write(size);

                        if (size > 0)
                        {
                            char[] txts = brTex.ReadChars(Convert.ToInt32(size));

                            string[] textures = new string(txts).Split('\0');
                            
                            // 1 less because of empty string at the end
                            for(int i = 0; i < textures.Length - 1; i++)
                                textureList.Add(textures[i]);

                            bwOut.Write(txts);
                        }
                    }
                    else if (header.SequenceEqual(chMcnk))
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

                                    brTex.ReadUInt32(); // Skip and replace groundeffect
                                    GetTextureInfo(curAdtName, textureList[Convert.ToInt32(textureId)],
                                        out TextureInfo tInfo);

                                    bwOut.Write(tInfo.GroundEffect);
                                }
                            }
                            else if (subHeader.SequenceEqual(chMcal))
                            {
                                bwOut.Write(subHeader);
                                bwOut.Write(subSize);

                                byte[] layerData = brTex.ReadBytes(Convert.ToInt32(subSize));

                                bwOut.Write(layerData);

                                byte[] doodadsMapData = new byte[16];

                                uint mcalsToRead = subSize / 4096;

                                int[] highestDoodadMcal = new int[64];
                                uint[] highestDoodadMcalAmt =  new uint[64];

                                for(int mcalId = 0; mcalId < mcalsToRead; mcalId++)
                                { 
                                    // Foreach 8x8 piece of Data in the 4096 this layer composes
                                    for (int r = 0; r < 8; r++)
                                    {
                                        for (int i = 0; i < 8; i++)
                                        {
                                            // Check if the average is >= The cutoff value for spawning groundeffects

                                            // We're in : Console.WriteLine("Doodadset: {0},{1}", r, i);
                                            uint layerValue = 0;
                                            for (int j = 0; j < 8; j++)
                                            {
                                                for (int colId = 0; colId < 8; colId++)
                                                {
                                                    int layerDataId = (mcalId * 4096) + (r * 512) + (j * 64) + (i * 8) + colId;
                                                    layerValue += layerData[layerDataId];
                                                }
                                            }

                                            layerValue = layerValue / 64;

                                            // If so, set the bit in the map above corresponding to the layer we're at
                                            if (layerValue >= GroundEffectCutoffValue && layerValue >= highestDoodadMcalAmt[r*8 + i])
                                            {

                                                highestDoodadMcal[r * 8 + i] = mcalId + 1;
                                                highestDoodadMcalAmt[r * 8 + i] = layerValue;
                                            }
                                            
                                        }
                                        
                                    }


                                }

                                for (int i = 0; i < 8; i++)
                                {
                                    for (int j = 0; j < 8; j++)
                                    {
                                        byte bit = Convert.ToByte(highestDoodadMcal[i * 8 + j] << ((j % 4) * 2));
                                        doodadsMapData[j / 4 + (i * 2)] |= bit;
                                    }
                                }

                                /*for (int i = 0; i < 16; i++)
                                {
                                    doodadsMapData[i] = (byte) (((doodadsMapData[i] * 0x80200802) & 0x0884422110) * 0x0101010101 >> 32);
                                }*/
                                mcnkGroundEffectMaps.Add(doodadsMapData);
                            }
                            else // Trash chunk we don't use
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
                bwOut.Write((uint) 64);

                for (int i = 0; i < textureList.Count; i++)
                {
                    GetTextureInfo(curAdtName, textureList[i], out TextureInfo txInfo);

                    bwOut.Write(txInfo.GetFlags());
                    bwOut.Write(txInfo.HeightScale);
                    bwOut.Write(txInfo.HeightOffset);
                    bwOut.Write((uint) 0); // Padding?
                }

                long endPos = bwOut.BaseStream.Position;
                bwOut.BaseStream.Position = mtxpLenPos;
                bwOut.Write(Convert.ToUInt32(endPos - mtxpLenPos) - 4);

                
                bwOut.Close();

                string mainAdtName = curAdtName + ".adt";
                try
                {
                    BinaryReader br = new BinaryReader(File.OpenRead("Input\\" + mainAdtName));
                    // We now have the full groundeffects map, let's edit the main ADT accordingly.
                    BinaryWriter bw = new BinaryWriter(File.OpenWrite("Output\\" + mainAdtName));

                    // Header
                    bw.Write(br.ReadBytes(12));

                    int curMcnk = 0;
                    while(br.BaseStream.Position < br.BaseStream.Length)
                    {
                        char[] headerChars = br.ReadChars(4);
                        uint size = br.ReadUInt32();

                        bw.Write(headerChars);
                        bw.Write(size);
                        
                        if (headerChars.SequenceEqual(chMcnk))
                        {
                            // Other stuff
                            bw.Write(br.ReadBytes(64));

                            br.BaseStream.Position += 16;
                            bw.Write(mcnkGroundEffectMaps[curMcnk]);
                            bw.Write(br.ReadBytes(Convert.ToInt32(size - 80)));
                            curMcnk++;
                        }
                        else
                        {
                            bw.Write(br.ReadBytes(Convert.ToInt32(size)));
                        }
                    }
                }
                catch (Exception e)
                {
                    Console.ForegroundColor = ConsoleColor.Red;

                    Console.WriteLine("Error opening {0} : \r\n {1}", mainAdtName, e.Message);

                    Console.ReadLine();
                }
                Console.WriteLine(curAdtName + " Done!");
            }
        }
    }
}
