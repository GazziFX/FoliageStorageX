using System;
using System.IO;
using UnityEngine;

namespace FoliageStorageX
{
    public static class Extensions
    {
        public static float ReadHalf(this BinaryReader reader)
        {
            return Mathf.HalfToFloat(reader.ReadUInt16());
        }

        public static Vector3 ReadHalfVector3(this BinaryReader reader)
        {
            return new Vector3(reader.ReadHalf(), reader.ReadHalf(), reader.ReadHalf());
        }

        public static void WriteHalf(this BinaryWriter writer, float value)
        {
            writer.Write(Mathf.FloatToHalf(value));
        }

        public static void WriteHalfVector3(this BinaryWriter writer, Vector3 value)
        {
            writer.WriteHalf(value.x);
            writer.WriteHalf(value.y);
            writer.WriteHalf(value.z);
        }
    }
}
