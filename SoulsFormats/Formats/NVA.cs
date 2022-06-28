using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;

namespace SoulsFormats
{
    /// <summary>
    /// A file that defines the placement and properties of navmeshes in BB, DS3, and Sekiro. Extension: .nva
    /// </summary>
    public class NVA : SoulsFile<NVA>
    {
        /// <summary>
        /// Version of the overall format.
        /// </summary>
        public enum NVAVersion : uint
        {
            /// <summary>
            /// Used for a single BB test map, m29_03_10_00; has no Section8
            /// </summary>
            OldBloodborne = 3,

            /// <summary>
            /// Dark Souls 3 and Bloodborne
            /// </summary>
            DarkSouls3 = 4,

            /// <summary>
            /// Sekiro
            /// </summary>
            Sekiro = 5,
        }

        /// <summary>
        /// The format version of this file.
        /// </summary>
        public NVAVersion Version { get; set; }

        /// <summary>
        /// Navmesh instances in the map.
        /// </summary>
        public NavmeshSection NavMeshes { get; set; }

        /// <summary>
        /// Unknown.
        /// </summary>
        public FaceDataSection FaceDataSets { get; set; }

        /// <summary>
        /// Unknown.
        /// </summary>
        public NodeBankSection NodeBanks { get; set; }

        /// <summary>
        /// Connections between different navmeshes.
        /// </summary>
        public ConnectorSection Connectors { get; set; }

        /// <summary>
        /// Unknown.
        /// </summary>
        public LevelConnectorSection LevelConnectors { get; set; }

        /// <summary>
        /// Creates an empty NVA formatted for DS3.
        /// </summary>
        public NVA()
        {
            Version = NVAVersion.DarkSouls3;
            NavMeshes = new NavmeshSection(2);
            FaceDataSets = new FaceDataSection();
            NodeBanks = new NodeBankSection();
            Connectors = new ConnectorSection();
            LevelConnectors = new LevelConnectorSection();
        }

        /// <summary>
        /// Checks whether the data appears to be a file of this format.
        /// </summary>
        protected override bool Is(BinaryReaderEx br)
        {
            if (br.Length < 4)
                return false;

            string magic = br.GetASCII(0, 4);
            return magic == "NVMA";
        }

        /// <summary>
        /// Deserializes file data from a stream.
        /// </summary>
        protected override void Read(BinaryReaderEx br)
        {
            br.BigEndian = false;
            br.AssertASCII("NVMA");
            Version = br.ReadEnum32<NVAVersion>();
            br.ReadUInt32(); // File size
            br.AssertInt32(Version == NVAVersion.OldBloodborne ? 8 : 9); // Section count

            NavMeshes = new NavmeshSection(br);
            FaceDataSets = new FaceDataSection(br);
            NodeBanks = new NodeBankSection(br);
            new Section3(br);
            Connectors = new ConnectorSection(br);
            var connectorPoints = new ConnectorPointSection(br);
            var connectorConditions = new GraphConnectionSection(br);
            LevelConnectors = new LevelConnectorSection(br);
            GateNodeSection mapNodes;
            if (Version == NVAVersion.OldBloodborne)
                mapNodes = new GateNodeSection(1);
            else
                mapNodes = new GateNodeSection(br);

            foreach (Navmesh navmesh in NavMeshes)
                navmesh.TakeMapNodes(mapNodes);

            foreach (Connector connector in Connectors)
                connector.TakePointsAndConds(connectorPoints, connectorConditions);
        }

        /// <summary>
        /// Serializes file data to a stream.
        /// </summary>
        protected override void Write(BinaryWriterEx bw)
        {
            var connectorPoints = new ConnectorPointSection();
            var connectorConditions = new GraphConnectionSection();
            foreach (Connector connector in Connectors)
                connector.GivePointsAndConds(connectorPoints, connectorConditions);

            var mapNodes = new GateNodeSection(Version == NVAVersion.Sekiro ? 2 : 1);
            foreach (Navmesh navmesh in NavMeshes)
                navmesh.GiveMapNodes(mapNodes);

            bw.BigEndian = false;
            bw.WriteASCII("NVMA");
            bw.WriteUInt32((uint)Version);
            bw.ReserveUInt32("FileSize");
            bw.WriteInt32(Version == NVAVersion.OldBloodborne ? 8 : 9);

            NavMeshes.Write(bw, 0);
            FaceDataSets.Write(bw, 1);
            NodeBanks.Write(bw, 2);
            new Section3().Write(bw, 3);
            Connectors.Write(bw, 4);
            connectorPoints.Write(bw, 5);
            connectorConditions.Write(bw, 6);
            LevelConnectors.Write(bw, 7);
            if (Version != NVAVersion.OldBloodborne)
                mapNodes.Write(bw, 8);

            bw.FillUInt32("FileSize", (uint)bw.Position);
        }

        /// <summary>
        /// NVA is split up into 8 lists of different types.
        /// </summary>
        public abstract class Section<T> : List<T>
        {
            /// <summary>
            /// A version number indicating the format of the section. Don't change this unless you know what you're doing.
            /// </summary>
            public int Version { get; set; }

            internal Section(int version) : base()
            {
                Version = version;
            }

            internal Section(BinaryReaderEx br, int index, params int[] versions) : base()
            {
                br.AssertInt32(index);
                Version = br.AssertInt32(versions);
                int length = br.ReadInt32();
                int count = br.ReadInt32();
                Capacity = count;

                long start = br.Position;
                ReadEntries(br, count);
                br.Position = start + length;
            }

            internal abstract void ReadEntries(BinaryReaderEx br, int count);

            internal void Write(BinaryWriterEx bw, int index)
            {
                bw.WriteInt32(index);
                bw.WriteInt32(Version);
                bw.ReserveInt32("SectionLength");
                bw.WriteInt32(Count);

                long start = bw.Position;
                WriteEntries(bw);
                if (bw.Position % 0x10 != 0)
                    bw.WritePattern(0x10 - (int)bw.Position % 0x10, 0xFF);
                bw.FillInt32("SectionLength", (int)(bw.Position - start));
            }

            internal abstract void WriteEntries(BinaryWriterEx bw);
        }

        /// <summary>
        /// A list of navmesh instances. Version: 2 for DS3 and the BB test map, 3 for BB, 4 for Sekiro.
        /// </summary>
        public class NavmeshSection : Section<Navmesh>
        {
            /// <summary>
            /// Creates an empty NavmeshSection with the given version.
            /// </summary>
            public NavmeshSection(int version) : base(version) { }

            internal NavmeshSection(BinaryReaderEx br) : base(br, 0, 2, 3, 4) { }

            internal override void ReadEntries(BinaryReaderEx br, int count)
            {
                for (int i = 0; i < count; i++)
                    Add(new Navmesh(br, Version));
            }

            internal override void WriteEntries(BinaryWriterEx bw)
            {
                for (int i = 0; i < Count; i++)
                    this[i].Write(bw, Version, i);

                for (int i = 0; i < Count; i++)
                    this[i].WriteNameRefs(bw, Version, i);
            }
        }

        /// <summary>
        /// An instance of a navmesh.
        /// </summary>
        public class Navmesh
        {
            /// <summary>
            /// Position of the mesh.
            /// </summary>
            public Vector3 Position { get; set; }

            /// <summary>
            /// Rotation of the mesh, in radians.
            /// </summary>
            public Vector3 Rotation { get; set; }

            /// <summary>
            /// Scale of the mesh.
            /// </summary>
            public Vector3 Scale { get; set; }

            /// <summary>
            /// Unknown.
            /// </summary>
            public int NameID { get; set; }

            /// <summary>
            /// Unknown.
            /// </summary>
            public int ModelID { get; set; }

            /// <summary>
            /// Unknown.
            /// </summary>
            public int FaceDataIdx { get; set; }

            /// <summary>
            /// Face count of the navmesh model
            /// </summary>
            public int FaceCount { get; set; }

            /// <summary>
            /// Unknown.
            /// </summary>
            public List<int> NameReferenceIDs { get; set; }

            /// <summary>
            /// Gate (exit) nodes of this navmesh
            /// </summary>
            public List<GateNode> GateNodes { get; set; }

            /// <summary>
            /// Unknown
            /// </summary>
            public bool Unk4C { get; set; }

            private short GateNodeIdx;
            private short GateNodeCount;

            /// <summary>
            /// Creates a Navmesh with default values.
            /// </summary>
            public Navmesh()
            {
                Scale = Vector3.One;
                NameReferenceIDs = new List<int>();
                GateNodes = new List<GateNode>();
            }

            internal Navmesh(BinaryReaderEx br, int version)
            {
                Position = br.ReadVector3();
                br.AssertSingle(1);
                Rotation = br.ReadVector3();
                br.AssertInt32(0);
                Scale = br.ReadVector3();
                br.AssertInt32(0);
                NameID = br.ReadInt32();
                ModelID = br.ReadInt32();
                FaceDataIdx = br.ReadInt32();
                br.AssertInt32(0);
                FaceCount = br.ReadInt32();
                int nameRefCount = br.ReadInt32();
                GateNodeIdx = br.ReadInt16();
                GateNodeCount = br.ReadInt16();
                Unk4C = br.AssertInt32(0, 1) == 1;

                if (version < 4)
                {
                    if (nameRefCount > 16)
                        throw new InvalidDataException("Name reference count should not exceed 16 in DS3/BB.");
                    NameReferenceIDs = new List<int>(br.ReadInt32s(nameRefCount));
                    for (int i = 0; i < 16 - nameRefCount; i++)
                        br.AssertInt32(-1);
                }
                else
                {
                    int nameRefOffset = br.ReadInt32();
                    br.AssertInt32(0);
                    br.AssertInt32(0);
                    br.AssertInt32(0);
                    NameReferenceIDs = new List<int>(br.GetInt32s(nameRefOffset, nameRefCount));
                }
            }

            internal void TakeMapNodes(GateNodeSection entries8)
            {
                GateNodes = new List<GateNode>(GateNodeCount);
                for (int i = 0; i < GateNodeCount; i++)
                    GateNodes.Add(entries8[GateNodeIdx + i]);
                GateNodeCount = -1;

                foreach (GateNode mapNode in GateNodes)
                {
                    if (mapNode.SiblingDistances.Count > GateNodes.Count)
                        mapNode.SiblingDistances.RemoveRange(GateNodes.Count, mapNode.SiblingDistances.Count - GateNodes.Count);
                }
            }

            internal void Write(BinaryWriterEx bw, int version, int index)
            {
                bw.WriteVector3(Position);
                bw.WriteSingle(1);
                bw.WriteVector3(Rotation);
                bw.WriteInt32(0);
                bw.WriteVector3(Scale);
                bw.WriteInt32(0);
                bw.WriteInt32(NameID);
                bw.WriteInt32(ModelID);
                bw.WriteInt32(FaceDataIdx);
                bw.WriteInt32(0);
                bw.WriteInt32(FaceCount);
                bw.WriteInt32(NameReferenceIDs.Count);
                bw.WriteInt16(GateNodeIdx);
                bw.WriteInt16((short)GateNodes.Count);
                bw.WriteInt32(Unk4C ? 1 : 0);

                if (version < 4)
                {
                    if (NameReferenceIDs.Count > 16)
                        throw new InvalidDataException("Name reference count should not exceed 16 in DS3/BB.");
                    bw.WriteInt32s(NameReferenceIDs);
                    for (int i = 0; i < 16 - NameReferenceIDs.Count; i++)
                        bw.WriteInt32(-1);
                }
                else
                {
                    bw.ReserveInt32($"NameRefOffset{index}");
                    bw.WriteInt32(0);
                    bw.WriteInt32(0);
                    bw.WriteInt32(0);
                }
            }

            internal void WriteNameRefs(BinaryWriterEx bw, int version, int index)
            {
                if (version >= 4)
                {
                    bw.FillInt32($"NameRefOffset{index}", (int)bw.Position);
                    bw.WriteInt32s(NameReferenceIDs);
                }
            }

            internal void GiveMapNodes(GateNodeSection mapNodes)
            {
                // Sometimes when the map node count is 0 the index is also 0,
                // but usually this is accurate.
                GateNodeIdx = (short)mapNodes.Count;
                mapNodes.AddRange(GateNodes);
            }

            /// <summary>
            /// Returns a string representation of the navmesh.
            /// </summary>
            public override string ToString()
            {
                return $"{NameID} {Position} {Rotation} [{NameReferenceIDs.Count} References] [{GateNodes.Count} MapNodes]";
            }
        }

        /// <summary>
        /// Unknown.
        /// </summary>
        public class FaceDataSection : Section<FaceDataEntry>
        {
            /// <summary>
            /// Creates an empty Section1.
            /// </summary>
            public FaceDataSection() : base(1) { }

            internal FaceDataSection(BinaryReaderEx br) : base(br, 1, 1) { }

            internal override void ReadEntries(BinaryReaderEx br, int count)
            {
                for (int i = 0; i < count; i++)
                    Add(new FaceDataEntry(br));
            }

            internal override void WriteEntries(BinaryWriterEx bw)
            {
                foreach (FaceDataEntry entry in this)
                    entry.Write(bw);
            }
        }

        /// <summary>
        /// Unknown.
        /// </summary>
        public class FaceDataEntry
        {
            /// <summary>
            /// Unknown; always 0 in DS3 and SDT, sometimes 1 in BB.
            /// </summary>
            public int Unk00 { get; set; }

            /// <summary>
            /// Creates an Entry1 with default values.
            /// </summary>
            public FaceDataEntry() { }

            internal FaceDataEntry(BinaryReaderEx br)
            {
                Unk00 = br.ReadInt32();
                br.AssertInt32(0);
            }

            internal void Write(BinaryWriterEx bw)
            {
                bw.WriteInt32(Unk00);
                bw.WriteInt32(0);
            }

            /// <summary>
            /// Returns a string representation of the entry.
            /// </summary>
            public override string ToString()
            {
                return $"{Unk00}";
            }
        }

        /// <summary>
        /// Unknown.
        /// </summary>
        public class NodeBankSection : Section<NodeBankEntry>
        {
            /// <summary>
            /// Creates an empty Section2.
            /// </summary>
            public NodeBankSection() : base(1) { }

            internal NodeBankSection(BinaryReaderEx br) : base(br, 2, 1) { }

            internal override void ReadEntries(BinaryReaderEx br, int count)
            {
                for (int i = 0; i < count; i++)
                    Add(new NodeBankEntry(br));
            }

            internal override void WriteEntries(BinaryWriterEx bw)
            {
                foreach (NodeBankEntry entry in this)
                    entry.Write(bw);
            }
        }

        /// <summary>
        /// Seems to be for connecting navmeshes that aren't physically connected (elevators etc.)
        /// </summary>
        public class NodeBankEntry
        {
            /// <summary>
            /// Unknown; seems to just be the index of this entry.
            /// </summary>
            public int BankIdx { get; set; }

            /// <summary>
            /// References in this entry; maximum of 64.
            /// </summary>
            public List<NodeBankFace> NodeBankFaces { get; set; }

            /// <summary>
            /// Unknown.
            /// </summary>
            public int EntityId { get; set; }

            /// <summary>
            /// Creates an Entry2 with default values.
            /// </summary>
            public NodeBankEntry()
            {
                NodeBankFaces = new List<NodeBankFace>();
                EntityId = -1;
            }

            internal NodeBankEntry(BinaryReaderEx br)
            {
                BankIdx = br.ReadInt32();
                int referenceCount = br.ReadInt32();
                EntityId = br.ReadInt32();
                br.AssertInt32(0);
                if (referenceCount > 64)
                    throw new InvalidDataException("Entry2 reference count should not exceed 64.");

                NodeBankFaces = new List<NodeBankFace>(referenceCount);
                for (int i = 0; i < referenceCount; i++)
                    NodeBankFaces.Add(new NodeBankFace(br));

                for (int i = 0; i < 64 - referenceCount; i++)
                    br.AssertInt64(0);
            }

            internal void Write(BinaryWriterEx bw)
            {
                bw.WriteInt32(BankIdx);
                bw.WriteInt32(NodeBankFaces.Count);
                bw.WriteInt32(EntityId);
                bw.WriteInt32(0);
                if (NodeBankFaces.Count > 64)
                    throw new InvalidDataException("Entry2 reference count should not exceed 64.");

                foreach (NodeBankFace reference in NodeBankFaces)
                    reference.Write(bw);

                for (int i = 0; i < 64 - NodeBankFaces.Count; i++)
                    bw.WriteInt64(0);
            }

            /// <summary>
            /// Returns a string representation of the entry.
            /// </summary>
            public override string ToString()
            {
                return $"{BankIdx} {EntityId} [{NodeBankFaces.Count} References]";
            }

            /// <summary>
            /// Unknown.
            /// </summary>
            public class NodeBankFace
            {
                /// <summary>
                /// Unknown.
                /// </summary>
                public int FaceIdx { get; set; }

                /// <summary>
                /// Unknown.
                /// </summary>
                public int NameID { get; set; }

                /// <summary>
                /// Creates a Reference with defalt values.
                /// </summary>
                public NodeBankFace() { }

                internal NodeBankFace(BinaryReaderEx br)
                {
                    FaceIdx = br.ReadInt32();
                    NameID = br.ReadInt32();
                }

                internal void Write(BinaryWriterEx bw)
                {
                    bw.WriteInt32(FaceIdx);
                    bw.WriteInt32(NameID);
                }

                /// <summary>
                /// Returns a string representation of the reference.
                /// </summary>
                public override string ToString()
                {
                    return $"{FaceIdx} {NameID}";
                }
            }
        }

        private class Section3 : Section<Entry3>
        {
            public Section3() : base(1) { }

            internal Section3(BinaryReaderEx br) : base(br, 3, 1) { }

            internal override void ReadEntries(BinaryReaderEx br, int count)
            {
                for (int i = 0; i < count; i++)
                    Add(new Entry3(br));
            }

            internal override void WriteEntries(BinaryWriterEx bw)
            {
                foreach (Entry3 entry in this)
                    entry.Write(bw);
            }
        }

        private class Entry3
        {
            internal Entry3(BinaryReaderEx br)
            {
                throw new NotImplementedException("Section3 is empty in all known NVAs.");
            }

            internal void Write(BinaryWriterEx bw)
            {
                throw new NotImplementedException("Section3 is empty in all known NVAs.");
            }
        }

        /// <summary>
        /// A list of connections between navmeshes.
        /// </summary>
        public class ConnectorSection : Section<Connector>
        {
            /// <summary>
            /// Creates an empty ConnectorSection.
            /// </summary>
            public ConnectorSection() : base(1) { }

            internal ConnectorSection(BinaryReaderEx br) : base(br, 4, 1) { }

            internal override void ReadEntries(BinaryReaderEx br, int count)
            {
                for (int i = 0; i < count; i++)
                    Add(new Connector(br));
            }

            internal override void WriteEntries(BinaryWriterEx bw)
            {
                foreach (Connector entry in this)
                    entry.Write(bw);
            }
        }

        /// <summary>
        /// A connection between two navmeshes.
        /// </summary>
        public class Connector
        {
            /// <summary>
            /// Unknown.
            /// </summary>
            public int MainNameID { get; set; }

            /// <summary>
            /// The navmesh to be attached.
            /// </summary>
            public int TargetNameID { get; set; }

            /// <summary>
            /// Points used by this connection.
            /// </summary>
            public List<NavMeshConnection> Points { get; set; }

            /// <summary>
            /// Conditions used by this connection.
            /// </summary>
            public List<GraphConnection> Conditions { get; set; }

            private int PointCount;
            private int ConditionCount;
            private int PointsIndex;
            private int ConditionsIndex;

            /// <summary>
            /// Creates a Connector with default values.
            /// </summary>
            public Connector()
            {
                Points = new List<NavMeshConnection>();
                Conditions = new List<GraphConnection>();
            }

            internal Connector(BinaryReaderEx br)
            {
                MainNameID = br.ReadInt32();
                TargetNameID = br.ReadInt32();
                PointCount = br.ReadInt32();
                ConditionCount = br.ReadInt32();
                PointsIndex = br.ReadInt32();
                br.AssertInt32(0);
                ConditionsIndex = br.ReadInt32();
                br.AssertInt32(0);
            }

            internal void TakePointsAndConds(ConnectorPointSection points, GraphConnectionSection conds)
            {
                Points = new List<NavMeshConnection>(PointCount);
                for (int i = 0; i < PointCount; i++)
                    Points.Add(points[PointsIndex + i]);
                PointCount = -1;

                Conditions = new List<GraphConnection>(ConditionCount);
                for (int i = 0; i < ConditionCount; i++)
                    Conditions.Add(conds[ConditionsIndex + i]);
                ConditionCount = -1;
            }

            internal void GivePointsAndConds(ConnectorPointSection points, GraphConnectionSection conds)
            {
                PointsIndex = points.Count;
                points.AddRange(Points);

                ConditionsIndex = conds.Count;
                conds.AddRange(Conditions);
            }

            internal void Write(BinaryWriterEx bw)
            {
                bw.WriteInt32(MainNameID);
                bw.WriteInt32(TargetNameID);
                bw.WriteInt32(Points.Count);
                bw.WriteInt32(Conditions.Count);
                bw.WriteInt32(PointsIndex);
                bw.WriteInt32(0);
                bw.WriteInt32(ConditionsIndex);
                bw.WriteInt32(0);
            }

            /// <summary>
            /// Returns a string representation of the connector.
            /// </summary>
            public override string ToString()
            {
                return $"{MainNameID} -> {TargetNameID} [{Points.Count} Points][{Conditions.Count} Conditions]";
            }
        }

        /// <summary>
        /// A list of points used to connect navmeshes.
        /// </summary>
        internal class ConnectorPointSection : Section<NavMeshConnection>
        {
            /// <summary>
            /// Creates an empty ConnectorPointSection.
            /// </summary>
            public ConnectorPointSection() : base(1) { }

            internal ConnectorPointSection(BinaryReaderEx br) : base(br, 5, 1) { }

            internal override void ReadEntries(BinaryReaderEx br, int count)
            {
                for (int i = 0; i < count; i++)
                    Add(new NavMeshConnection(br));
            }

            internal override void WriteEntries(BinaryWriterEx bw)
            {
                foreach (NavMeshConnection entry in this)
                    entry.Write(bw);
            }
        }

        /// <summary>
        /// Resembles hkaiStreamingSet::NavMeshConnection, indicating where exactly navmeshes are connected
        /// </summary>
        public class NavMeshConnection
        {
            /// <summary>
            /// Unknown.
            /// </summary>
            public int FaceIdx { get; set; }

            /// <summary>
            /// Unknown.
            /// </summary>
            public int EdgeIdx { get; set; }

            /// <summary>
            /// Unknown.
            /// </summary>
            public int OppositeFaceIdx { get; set; }

            /// <summary>
            /// Unknown.
            /// </summary>
            public int OppositeEdgeIdx { get; set; }

            /// <summary>
            /// Creates a NavMeshConnection with default values.
            /// </summary>
            public NavMeshConnection() { }

            internal NavMeshConnection(BinaryReaderEx br)
            {
                FaceIdx = br.ReadInt32();
                EdgeIdx = br.ReadInt32();
                OppositeFaceIdx = br.ReadInt32();
                OppositeEdgeIdx = br.ReadInt32();
            }

            internal void Write(BinaryWriterEx bw)
            {
                bw.WriteInt32(FaceIdx);
                bw.WriteInt32(EdgeIdx);
                bw.WriteInt32(OppositeFaceIdx);
                bw.WriteInt32(OppositeEdgeIdx);
            }

            /// <summary>
            /// Returns a string representation of the point.
            /// </summary>
            public override string ToString()
            {
                return $"{FaceIdx} {EdgeIdx} {OppositeFaceIdx} {OppositeEdgeIdx}";
            }
        }

        /// <summary>
        /// Section containing GraphConnections
        /// </summary>
        internal class GraphConnectionSection : Section<GraphConnection>
        {
            /// <summary>
            /// Creates an empty GraphConnectionSection.
            /// </summary>
            public GraphConnectionSection() : base(1) { }

            internal GraphConnectionSection(BinaryReaderEx br) : base(br, 6, 1) { }

            internal override void ReadEntries(BinaryReaderEx br, int count)
            {
                for (int i = 0; i < count; i++)
                    Add(new GraphConnection(br));
            }

            internal override void WriteEntries(BinaryWriterEx bw)
            {
                foreach (GraphConnection entry in this)
                    entry.Write(bw);
            }
        }

        /// <summary>
        /// Resembles hkaiStreamingSet::GraphConnection, indicating which nodes of the graph represent the connection
        /// </summary>
        public class GraphConnection
        {
            /// <summary>
            /// Unknown.
            /// </summary>
            public int NodeIdx { get; set; }

            /// <summary>
            /// Unknown.
            /// </summary>
            public int OppositeNodeIdx { get; set; }

            /// <summary>
            /// Creates a GraphConnection with default values.
            /// </summary>
            public GraphConnection() { }

            internal GraphConnection(BinaryReaderEx br)
            {
                NodeIdx = br.ReadInt32();
                OppositeNodeIdx = br.ReadInt32();
            }

            internal void Write(BinaryWriterEx bw)
            {
                bw.WriteInt32(NodeIdx);
                bw.WriteInt32(OppositeNodeIdx);
            }

            /// <summary>
            /// Returns a string representation of the connection.
            /// </summary>
            public override string ToString()
            {
                return $"{NodeIdx} {OppositeNodeIdx}";
            }
        }

        /// <summary>
        /// Hypothesis: This connects levels
        /// </summary>
        public class LevelConnectorSection : Section<LevelConnector>
        {
            /// <summary>
            /// Creates an empty LevelConnectorSection.
            /// </summary>
            public LevelConnectorSection() : base(1) { }

            internal LevelConnectorSection(BinaryReaderEx br) : base(br, 7, 1) { }

            internal override void ReadEntries(BinaryReaderEx br, int count)
            {
                for (int i = 0; i < count; i++)
                    Add(new LevelConnector(br));
            }

            internal override void WriteEntries(BinaryWriterEx bw)
            {
                foreach (LevelConnector entry in this)
                    entry.Write(bw);
            }
        }

        /// <summary>
        /// Unknown; believed to have something to do with connecting maps.
        /// </summary>
        public class LevelConnector
        {
            /// <summary>
            /// Unknown.
            /// </summary>
            public Vector3 Position { get; set; }

            /// <summary>
            /// Unknown.
            /// </summary>
            public int NameID { get; set; }

            /// <summary>
            /// Unknown.
            /// </summary>
            public int Unk18 { get; set; }

            /// <summary>
            /// Creates an Entry7 with default values.
            /// </summary>
            public LevelConnector() { }

            internal LevelConnector(BinaryReaderEx br)
            {
                Position = br.ReadVector3();
                br.AssertSingle(1);
                NameID = br.ReadInt32();
                br.AssertInt32(0);
                Unk18 = br.ReadInt32();
                br.AssertInt32(0);
            }

            internal void Write(BinaryWriterEx bw)
            {
                bw.WriteVector3(Position);
                bw.WriteSingle(1);
                bw.WriteInt32(NameID);
                bw.WriteInt32(0);
                bw.WriteInt32(Unk18);
                bw.WriteInt32(0);
            }

            /// <summary>
            /// Returns a string representation of the entry.
            /// </summary>
            public override string ToString()
            {
                return $"{Position} {NameID} {Unk18}";
            }
        }

        /// <summary>
        /// This contains the nodes of the graph indicating how navmeshes are connected. Probably a ripoff of hkaiDirectedGraphExplicitCost
        /// </summary>
        internal class GateNodeSection : Section<GateNode>
        {
            /// <summary>
            /// Creates an empty Section8 with the given version.
            /// </summary>
            public GateNodeSection(int version) : base(version) { }

            internal GateNodeSection(BinaryReaderEx br) : base(br, 8, 1, 2) { }

            internal override void ReadEntries(BinaryReaderEx br, int count)
            {
                for (int i = 0; i < count; i++)
                    Add(new GateNode(br, Version));
            }

            internal override void WriteEntries(BinaryWriterEx bw)
            {
                for (int i = 0; i < Count; i++)
                    this[i].Write(bw, Version, i);

                for (int i = 0; i < Count; i++)
                    this[i].WriteSubIDs(bw, Version, i);
            }
        }

        /// <summary>
        /// I've renamed this to what it was called in DS1
        /// </summary>
        public class GateNode
        {
            /// <summary>
            /// Unknown.
            /// </summary>
            public Vector3 Position { get; set; }

            /// <summary>
            /// Index of the connected navmesh.
            /// </summary>
            public short ConnectedNavmeshIdx { get; set; }

            /// <summary>
            /// Too lazy to rename. It's the index of the connection to the connected navmesh.
            /// </summary>
            public short MainID { get; set; }

            /// <summary>
            /// Costs between gate nodes of a given navmesh.
            /// This doesn't appear to be Euclidean distance. It's probably the distance an agent has to cover on the actual navmesh.
            /// </summary>
            public List<float> SiblingDistances { get; set; }

            /// <summary>
            /// Unknown; only present in Sekiro.
            /// </summary>
            public int Unk14 { get; set; }

            /// <summary>
            /// Creates a GateNode with default values.
            /// </summary>
            public GateNode()
            {
                SiblingDistances = new List<float>();
            }

            internal GateNode(BinaryReaderEx br, int version)
            {
                Position = br.ReadVector3();
                ConnectedNavmeshIdx = br.ReadInt16();
                MainID = br.ReadInt16();

                if (version < 2)
                {
                    SiblingDistances = new List<float>(
                        br.ReadUInt16s(16).Select(s => s == 0xFFFF ? -1 : s * 0.01f));
                }
                else
                {
                    int subIDCount = br.ReadInt32();
                    Unk14 = br.ReadInt32();
                    int subIDsOffset = br.ReadInt32();
                    br.AssertInt32(0);
                    SiblingDistances = new List<float>(
                        br.GetUInt16s(subIDsOffset, subIDCount).Select(s => s == 0xFFFF ? -1 : s * 0.01f));
                }
            }

            internal void Write(BinaryWriterEx bw, int version, int index)
            {
                bw.WriteVector3(Position);
                bw.WriteInt16(ConnectedNavmeshIdx);
                bw.WriteInt16(MainID);

                if (version < 2)
                {
                    if (SiblingDistances.Count > 16)
                        throw new InvalidDataException("GateNode distance count must not exceed 16 in DS3/BB.");

                    foreach (float distance in SiblingDistances)
                        bw.WriteUInt16((ushort)(distance == -1 ? 0xFFFF : Math.Round(distance * 100)));

                    for (int i = 0; i < 16 - SiblingDistances.Count; i++)
                        bw.WriteUInt16(0xFFFF);
                }
                else
                {
                    bw.WriteInt32(SiblingDistances.Count);
                    bw.WriteInt32(Unk14);
                    bw.ReserveInt32($"SubIDsOffset{index}");
                    bw.WriteInt32(0);
                }
            }

            internal void WriteSubIDs(BinaryWriterEx bw, int version, int index)
            {
                if (version >= 2)
                {
                    bw.FillInt32($"SubIDsOffset{index}", (int)bw.Position);
                    foreach (float distance in SiblingDistances)
                        bw.WriteUInt16((ushort)(distance == -1 ? 0xFFFF : Math.Round(distance * 100)));
                }
            }

            /// <summary>
            /// Returns a string representation of the entry.
            /// </summary>
            public override string ToString()
            {
                return $"{Position} {ConnectedNavmeshIdx} {MainID} [{SiblingDistances.Count} SubIDs]";
            }
        }
    }
}
