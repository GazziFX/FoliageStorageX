using System;
using System.Collections.Generic;
using System.IO;
using SDG.Framework.Foliage;
using SDG.Unturned;
using UnityEngine;

namespace FoliageStorageX
{
    public class FoliageStorageV3 : IFoliageStorage
    {
        private const string FILEPATH = "/FoliageV3.blob";

        private readonly int FOLIAGE_FILE_VERSION = 1;

        private byte[] GUID_BUFFER = new byte[16];

        private FileStream readerStream;

        private BinaryReader reader;

        private bool hasAllTilesInMemory;

        private LinkedList<FoliageTile> pendingLoad = new LinkedList<FoliageTile>();

        private Dictionary<FoliageCoord, long> tileBlobOffsets = new Dictionary<FoliageCoord, long>();

        private long tileBlobHeaderOffset;

        public void initialize()
        {
            tileBlobOffsets.Clear();
            tileBlobHeaderOffset = 0L;
            string path = Level.info.path + FILEPATH;
            if (File.Exists(path))
            {
                readerStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                reader = new BinaryReader(readerStream);
                reader.ReadInt32();
                int num = reader.ReadInt32();
                UnturnedLog.info("Found {0} foliage v3 tiles", new object[] { num });
                for (int i = 0; i < num; i++)
                {
                    int x = reader.ReadInt32();
                    int y = reader.ReadInt32();
                    long offset = reader.ReadInt64(); // Why we need to support files over 4GB?
                    tileBlobOffsets.Add(new FoliageCoord(x, y), offset);
                }
                tileBlobHeaderOffset = readerStream.Position;
            }
        }

        public void shutdown()
        {
            closeReader();
        }

        public void tileBecameRelevantToViewer(FoliageTile tile)
        {
            if (!hasAllTilesInMemory)
            {
                pendingLoad.AddLast(tile);
            }
        }

        public void tileNoLongerRelevantToViewer(FoliageTile tile)
        {
            if (!hasAllTilesInMemory)
            {
                pendingLoad.Remove(tile);
                tile.clearAndReleaseInstances();
            }
        }

        public void tick()
        {
            if (pendingLoad.Count > 0)
            {
                FoliageTile value = pendingLoad.First.Value;
                pendingLoad.RemoveFirst();
                deserializeTile(value);
            }
        }

        public void editorLoadAllTiles(IEnumerable<FoliageTile> tiles)
        {
            hasAllTilesInMemory = true;
            foreach (FoliageTile foliageTile in tiles)
            {
                deserializeTile(foliageTile);
            }
            closeReader();
        }

        public void editorSaveAllTiles(IEnumerable<FoliageTile> tiles)
        {
            string path = Level.info.path + FILEPATH;
            if (File.Exists(path))
            {
                bool flag = false;
                foreach (var tile in tiles)
                {
                    if (tile.hasUnsavedChanges)
                    {
                        flag = true;
                        break;
                    }
                }
                if (!flag)
                {
                    return;
                }
            }
            UnturnedLog.info("Saving foliage v3");
            List<byte[]> list = new List<byte[]>();
            tileBlobOffsets.Clear();
            long num = 0L;
            foreach (FoliageTile foliageTile in tiles)
            {
                if (!foliageTile.isEmpty())
                {
                    byte[] array = serializeTile(foliageTile);
                    if (array != null && array.Length != 0)
                    {
                        list.Add(array);
                        tileBlobOffsets.Add(foliageTile.coord, num);
                        num += array.Length;
                    }
                }
            }
            if (list.Count != tileBlobOffsets.Count)
            {
                UnturnedLog.error("Foliage blob count ({0}) does not match offset count ({1})", new object[]
                {
                    list.Count,
                    tileBlobOffsets.Count
                });
                return;
            }
            using (FileStream fileStream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.Write, FileShare.ReadWrite))
            {
                BinaryWriter binaryWriter = new BinaryWriter(fileStream);
                binaryWriter.Write(FOLIAGE_FILE_VERSION);
                binaryWriter.Write(tileBlobOffsets.Count);
                foreach (KeyValuePair<FoliageCoord, long> keyValuePair in tileBlobOffsets)
                {
                    binaryWriter.Write(keyValuePair.Key.x);
                    binaryWriter.Write(keyValuePair.Key.y);
                    binaryWriter.Write(keyValuePair.Value);
                }
                foreach (byte[] array2 in list)
                {
                    binaryWriter.Write(array2);
                }
            }
        }

        protected void deserializeTile(FoliageTile tile)
        {
            long num;
            if (tileBlobOffsets.TryGetValue(tile.coord, out num))
            {
                readerStream.Position = tileBlobHeaderOffset + num;
                Vector3 tilePos = getTileCenter(tile.coord);
                int num2 = reader.ReadInt32();
                for (int i = 0; i < num2; i++)
                {
                    // GuidBuffer is internal :(
                    readerStream.Read(GUID_BUFFER, 0, 16);
                    AssetReference<FoliageInstancedMeshInfoAsset> assetReference = new AssetReference<FoliageInstancedMeshInfoAsset>(new Guid(GUID_BUFFER));
                    FoliageInstanceList orAddList = tile.getOrAddList(assetReference);
                    int num3 = reader.ReadInt32();
                    for (int j = 0; j < num3; j++)
                    {
                        float x = reader.ReadHalf();
                        float z = reader.ReadHalf();
                        float y = reader.ReadSingle();
                        Vector3 position = new Vector3(tilePos.x + x, y, tilePos.z + z);
                        Quaternion rotation = Quaternion.Euler(reader.ReadHalfVector3());
                        Vector3 scale = reader.ReadHalfVector3();
                        bool flag = reader.ReadBoolean();
                        if (!tile.isInstanceCut(position))
                        {
                            orAddList.addInstanceAppend(new FoliageInstanceGroup(assetReference, Matrix4x4.TRS(position, rotation, scale), flag));
                        }
                    }
                }
            }
            tile.updateBounds();
        }

        protected byte[] serializeTile(FoliageTile tile)
        {
            Vector3 tilePos = getTileCenter(tile.coord);
            byte[] array;
            using (MemoryStream memoryStream = new MemoryStream())
            {
                BinaryWriter binaryWriter = new BinaryWriter(memoryStream);
                binaryWriter.Write(tile.instances.Count);
                foreach (KeyValuePair<AssetReference<FoliageInstancedMeshInfoAsset>, FoliageInstanceList> keyValuePair in tile.instances)
                {
                    // GuidBuffer is internal :(
                    memoryStream.Write(keyValuePair.Key.GUID.ToByteArray(), 0, 16);
                    int num = 0;
                    foreach (List<Matrix4x4> matrices in keyValuePair.Value.matrices)
                    {
                        num += matrices.Count;
                    }
                    binaryWriter.Write(num);
                    for (int i = 0; i < keyValuePair.Value.matrices.Count; i++)
                    {
                        List<Matrix4x4> matrices = keyValuePair.Value.matrices[i];
                        List<bool> clearWhenBaked = keyValuePair.Value.clearWhenBaked[i];
                        for (int j = 0; j < matrices.Count; j++)
                        {
                            Matrix4x4 matrix = matrices[j];

                            // use "local" space to reduce effect of half precision
                            Vector3 localPos = matrix.GetPosition() - tilePos;

                            binaryWriter.WriteHalf(localPos.x);
                            binaryWriter.WriteHalf(localPos.z);
                            binaryWriter.Write(localPos.y); // keep y precision because it can be far away

                            Vector3 eulerAngles = matrix.GetRotation().eulerAngles;
                            binaryWriter.WriteHalfVector3(eulerAngles);

                            Vector3 scale = matrix.lossyScale;
                            binaryWriter.WriteHalfVector3(scale);

                            binaryWriter.Write(clearWhenBaked[j]);
                        }
                    }
                }
                array = memoryStream.ToArray();
            }
            return array;
        }

        private void closeReader()
        {
            if (reader != null)
            {
                reader.Close();
                reader.Dispose();
                reader = null;
            }
            if (readerStream != null)
            {
                readerStream.Close();
                readerStream.Dispose();
                readerStream = null;
            }
        }

        private static Vector3 getTileCenter(FoliageCoord coord)
        {
            float tileSize = FoliageSystem.TILE_SIZE;
            return new Vector3(coord.x * tileSize + tileSize * 0.5f, 0f, coord.y * tileSize + tileSize * 0.5f);
        }
    }
}
