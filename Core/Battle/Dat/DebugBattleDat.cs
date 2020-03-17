using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OpenVIII.Battle.Dat
{
    public class DebugBattleDat
    {
        private readonly byte[] _buffer;
        private const float BaseLineMaxYFilter = 10f;
        public const float ScaleHelper = 2048.0f;

        public DatFile DatFile { get; }

        /// <summary>
        /// Skeleton data section
        /// </summary>
        /// <param name="start"></param>
        private void ReadSection1(uint start) => Skeleton = Skeleton.CreateInstance(_br, start);

        public Skeleton Skeleton;

        #region Fields

        public Information Information;

        #endregion Fields

        #region Methods

        /// <summary>
        /// Information
        /// </summary>
        /// <param name="start"></param>
        /// <param name="br"></param>
        /// <param name="fileName"></param>
        private void ReadSection7(uint start) => Information = Information.CreateInstance(_br, start);

        #endregion Methods

        /// <summary>
        /// Section 2: Model Geometry
        /// </summary>
        /// <see cref="http://wiki.ffrtt.ru/index.php?title=FF8/FileFormat_DAT#Section_2:_Model_geometry"/>
        public Geometry Geometry;

        /// <summary>
        /// Model Geometry section
        /// </summary>
        /// <param name="start"></param>
        /// <param name="br"></param>
        /// <param name="fileName"></param>
        private void ReadSection2(uint start) => Geometry = Geometry.CreateInstance(_br, start);

        public Vector3 IndicatorPoint(Vector3 translationPosition)
        {
            if (_offsetYLow < 0)
                translationPosition.Y -= _offsetYLow;
            return _indicatorPoint + translationPosition;
        }

        /// <summary>
        /// This method returns geometry data AFTER animation matrix translations, local
        /// position/rotation translations. This is the final step of calculation. This data should
        /// be used only by Renderer. Any translations/vertices manipulation should happen inside
        /// this method or earlier
        /// </summary>
        /// <param name="objectId">
        /// Monsters can have more than one object. Treat it like multi-model geometry. They are all
        /// needed to build whole model
        /// </param>
        /// <param name="position">a Vector3 to set global position</param>
        /// <param name="rotation">a Quaternion to set the correct rotation. 1=90, 2=180 ...</param>
        /// <param name="animationId">an animation pointer. Animation 0 is always idle</param>
        /// <param name="animationFrame">
        /// an animation currentFrame from animation BattleID. You should pass incrementing currentFrame and reset to 0
        /// when frameCount max is hit
        /// </param>
        /// <param name="step">
        /// FEATURE: This float (0.0 - 1.0) is used in Linear interpolation in animation frames
        /// blending. 0.0 means frameN, 1.0 means FrameN+1. Usually this should be a result of
        /// deltaTime to see if computer is capable of rendering smooth animations rather than
        /// constant 15 FPS
        /// </param>
        /// <returns></returns>
        public VertexPositionTexturePointersGRP GetVertexPositions(int objectId, ref Vector3 translationPosition, Quaternion rotation, ref AnimationSystem refAnimationSystem, double step)
        {
            if (refAnimationSystem.AnimationFrame >= Animations[refAnimationSystem.AnimationId].Count || refAnimationSystem.AnimationFrame < 0)
                refAnimationSystem.AnimationFrame = 0;
            AnimationFrame nextFrame = Animations[refAnimationSystem.AnimationId][refAnimationSystem.AnimationFrame];
            int lastAnimationFrame = refAnimationSystem.LastAnimationFrame;
            IReadOnlyList<AnimationFrame> lastAnimationFrames = Animations[refAnimationSystem.LastAnimationId];
            lastAnimationFrame = lastAnimationFrames.Count > lastAnimationFrame ? lastAnimationFrame : lastAnimationFrames.Count - 1;
            AnimationFrame animationFrame = lastAnimationFrames[lastAnimationFrame];

            Object obj = Geometry.Objects[objectId];
            int i = 0;

            List<VectorBoneGRP> vectorBoneGroups = GetVertices(obj, animationFrame, nextFrame, step);
            //float minY = vertices.Min(x => x.Y);
            //Vector2 HLPTS = FindLowHighPoints(translationPosition, rotation, currentFrame, nextFrame, step);

            byte[] texturePointers = new byte[obj.CTriangles + obj.CQuads * 2];
            List<VertexPositionTexture> vpt = new List<VertexPositionTexture>(texturePointers.Length * 3);

            if (objectId == 0)
            {
                AnimationSystem animationSystem = refAnimationSystem;
                AnimationYOffset lastOffsets = _animationYOffsets?.First(x =>
                    x.ID == animationSystem.LastAnimationId &&
                    x.Frame == lastAnimationFrame) ?? default;
                AnimationYOffset nextOffsets = _animationYOffsets?.First(x =>
                    x.ID == animationSystem.AnimationId &&
                    x.Frame == animationSystem.AnimationFrame) ?? default;
                _offsetYLow = MathHelper.Lerp(lastOffsets.LowY, nextOffsets.LowY, (float)step);
                _indicatorPoint.X = MathHelper.Lerp(lastOffsets.MidX, nextOffsets.MidX, (float)step);
                _indicatorPoint.Y = MathHelper.Lerp(lastOffsets.HighY, nextOffsets.HighY, (float)step);
                _indicatorPoint.Z = MathHelper.Lerp(lastOffsets.MidZ, nextOffsets.MidZ, (float)step);
                // Move All Y axis down to 0 based on Lowest Y axis in Animation BattleID 0.
                if (OffsetY < 0)
                {
                    translationPosition.Y += OffsetY;
                }
                // If any Y axis readings are lower than 0 in Animation BattleID >0. Bring it up to zero.
            }

            //Triangle parsing
            for (; i < obj.CTriangles; i++)
            {
                Texture2D preVarTex = (Texture2D)Textures[obj.Triangles[i].TextureIndex];
                vpt.AddRange(obj.Triangles[i].GenerateVPT(vectorBoneGroups, rotation, translationPosition, preVarTex));
                texturePointers[i] = obj.Triangles[i].TextureIndex;
            }

            //Quad parsing
            for (i = 0; i < obj.CQuads; i++)
            {
                Texture2D preVarTex = (Texture2D)Textures[obj.Quads[i].TextureIndex];
                vpt.AddRange(obj.Quads[i].GenerateVPT(vectorBoneGroups, rotation, translationPosition, preVarTex));
                texturePointers[obj.CTriangles + i * 2] = obj.Quads[i].TextureIndex;
                texturePointers[obj.CTriangles + i * 2 + 1] = obj.Quads[i].TextureIndex;
            }

            return new VertexPositionTexturePointersGRP(vpt.ToArray(), texturePointers);
        }

        private List<VectorBoneGRP>
            GetVertices(Object @object, AnimationFrame currentFrame, AnimationFrame nextFrame, double step) => @object
            .VertexData.SelectMany(vertexData => vertexData.Vertices.Select(vertex =>
                CalculateFrame(new VectorBoneGRP(vertex, vertexData.BoneId), currentFrame, nextFrame, step))).ToList();

        private Vector4 FindLowHighPoints(Vector3 translationPosition, Quaternion rotation, AnimationFrame currentFrame,
            AnimationFrame nextFrame, double step)
        {
            List<VectorBoneGRP> vertices =
                Geometry.Objects.SelectMany(@object => GetVertices(@object, currentFrame, nextFrame, step)).ToList();
            if (translationPosition == Vector3.Zero && rotation == Quaternion.Identity)
                return new Vector4(vertices.Min(x => x.Y), vertices.Max(x => x.Y),
                    (vertices.Min(x => x.X) + vertices.Max(x => x.X)) / 2f,
                    (vertices.Min(x => x.Z) + vertices.Max(x => x.Z)) / 2f);
            {
                IEnumerable<Vector3> enumVertices = vertices
                    .Select(vertex => TransformVertex(vertex, translationPosition, rotation)).ToArray();
                // midpoints
                return new Vector4(enumVertices.Min(x => x.Y), enumVertices.Max(x => x.Y),
                    (enumVertices.Min(x => x.X) + enumVertices.Max(x => x.X)) / 2f,
                    (enumVertices.Min(x => x.Z) + enumVertices.Max(x => x.Z)) / 2f);
            }
        }

        public static Vector3 TransformVertex(Vector3 vertex, Vector3 localTranslate, Quaternion rotation) =>
            Vector3.Transform(Vector3.Transform(vertex, rotation), Matrix.CreateTranslation(localTranslate));

        /// <summary>
        /// Complex function that provides linear interpolation between two matrices of actual
        /// to-render animation currentFrame and next currentFrame data for blending
        /// </summary>
        /// <param name="vectorBoneGRP">the tuple that contains vertex and bone ident</param>
        /// <param name="currentFrame">current animation currentFrame to render</param>
        /// <param name="nextFrame">
        /// animation currentFrame to render that is AFTER the actual one. If last currentFrame, then usually 0 is
        /// the 'next' currentFrame
        /// </param>
        /// <param name="step">
        /// step is variable used to determinate the progress for linear interpolation. I.e. 0 for
        /// current currentFrame and 1 for next currentFrame; 0.5 for blend of two frames
        /// </param>
        /// <returns></returns>
        private VectorBoneGRP CalculateFrame(VectorBoneGRP vectorBoneGRP, AnimationFrame currentFrame,
            AnimationFrame nextFrame, double step)
        {
            Vector3 getVector(Matrix matrix)
            {
                Vector3 r = new Vector3(
                    matrix.M11 * vectorBoneGRP.X + matrix.M41 + matrix.M12 * vectorBoneGRP.Z + matrix.M13 * -vectorBoneGRP.Y,
                    matrix.M21 * vectorBoneGRP.X + matrix.M42 + matrix.M22 * vectorBoneGRP.Z + matrix.M23 * -vectorBoneGRP.Y,
                    matrix.M31 * vectorBoneGRP.X + matrix.M43 + matrix.M32 * vectorBoneGRP.Z + matrix.M33 * -vectorBoneGRP.Y);
                r = Vector3.Transform(r, Matrix.CreateScale(Skeleton.GetScale));
                return r;
            }
            Vector3 rootFramePos = getVector(currentFrame.BoneMatrix[vectorBoneGRP.BoneID]); //get's bone matrix
            if (!(step > 0f)) return new VectorBoneGRP(rootFramePos, vectorBoneGRP.BoneID);
            Vector3 nextFramePos = getVector(nextFrame.BoneMatrix[vectorBoneGRP.BoneID]);
            rootFramePos = Vector3.Lerp(rootFramePos, nextFramePos, (float)step);
            return new VectorBoneGRP(rootFramePos, vectorBoneGRP.BoneID);
        }

        /// <summary>
        /// Model Animation section
        /// </summary>
        /// <param name="start"></param>
        /// <param name="br"></param>
        /// <param name="fileName"></param>
        private void ReadSection3(uint start) => Animations = AnimationData.CreateInstance(_br, start, Skeleton);

        public AnimationData Animations;
        public int Frame;

        /// <summary>
        /// TIM - Textures
        /// </summary>
        /// <param name="start"></param>
        /// <param name="br"></param>
        /// <param name="fileName"></param>
        private void ReadSection11(uint start) => Textures = Textures.CreateInstance(_buffer, start, FileName);

        public Textures Textures;
        private BinaryReader _br;

        public DebugBattleDat(int fileId, EntityType entityType, int additionalFileId = -1, DebugBattleDat skeletonReference = null, Sections flags = Sections.All)
        {
            Flags = flags;

            ID = fileId;
            AltID = additionalFileId;

            Memory.Log.WriteLine($"{nameof(DebugBattleDat)}::{nameof(Load)}: Creating new BattleDat with {fileId},{entityType},{additionalFileId}");
            ArchiveBase aw = ArchiveWorker.Load(Memory.Archives.A_BATTLE);
            char et = entityType == EntityType.Weapon ? 'w' : entityType == EntityType.Character ? 'c' : default;
            string fileName = entityType == EntityType.Monster ? $"c0m{ID:D03}" :
                entityType == EntityType.Character || entityType == EntityType.Weapon ? $"d{fileId:x}{et}{additionalFileId:D03}"
                : string.Empty;
            this.EntityType = entityType;
            if (string.IsNullOrEmpty(fileName))
                return;
            string path;
            string search = "";
            if (et != default)
            {
                search = $"d{fileId:x}{et}";
                IEnumerable<string> test = aw.GetListOfFiles().Where(x => x.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0).ToArray();
                path = test.FirstOrDefault(x => x.ToLower().Contains(fileName));

                if (string.IsNullOrWhiteSpace(path) && test.Any() && entityType == EntityType.Character)
                    path = test.First();
            }
            else path = aw.GetListOfFiles().FirstOrDefault(x => x.ToLower().Contains(fileName));
            //Debug.Assert("c:\\ff8\\data\\ger\\battle\\d0w007.dat" != path);
            FileName = fileName;
            if (!string.IsNullOrWhiteSpace(path))
                _buffer = aw.GetBinaryFile(path);
            if (_buffer == null || _buffer.Length < 0)
            {
                Memory.Log.WriteLine($"Search String: {search} Not Found skipping {entityType}; So resulting file buffer is null.");
                return;
            }
            ExportFile();
            using (_br = new BinaryReader(new MemoryStream(_buffer)))
            {
                DatFile = DatFile.CreateInstance(_br);
            }

            LoadFile(skeletonReference);
            FindAllLowHighPoints();
        }

        /// <summary>
        /// Creates new instance of DAT class that provides every sections parsed into structs and
        /// helper functions for renderer
        /// </summary>
        /// <param name="fileId">This number is used in c0m(fileId) or d(fileId)cXYZ</param>
        /// <param name="entityType">Supply Monster, character or weapon (0,1,2)</param>
        /// <param name="additionalFileId">Used only in character or weapon to supply for d(fileId)[c/w](additionalFileId)</param>
        public static DebugBattleDat Load(int fileId, EntityType entityType, int additionalFileId = -1, DebugBattleDat skeletonReference = null, Sections flags = Sections.All)
            => new DebugBattleDat(fileId, entityType, additionalFileId, skeletonReference, flags);

        private void FindAllLowHighPoints()
        {
            if (EntityType != EntityType.Character && EntityType != EntityType.Monster) return;
            // Get baseline from running function on only Animation 0;
            if (Animations.Count == 0)
                return;
            List<Vector4> baseline = Animations[0].Select(x => FindLowHighPoints(Vector3.Zero, Quaternion.Identity, x, x, 0f)).ToList();
            //X is lowY, Y is high Y, Z is mid x, W is mid z
            (float baselineLowY, float baselineHighY) = (baseline.Min(x => x.X), baseline.Max(x => x.Y));
            OffsetY = 0f;
            if (Math.Abs(baselineLowY) < BaseLineMaxYFilter)
            {
                OffsetY -= baselineLowY;
                baselineHighY += OffsetY;
            }
            // Default indicator point
            _indicatorPoint = new Vector3(0, baselineHighY, 0);
            // Need to add this later to bring baseline low to 0.
            //OffsetY = offsetY;
            // Brings all Y values less than baseline low to baseline low
            _animationYOffsets = Animations.SelectMany((animation, animationID) =>
                animation.Select((animationFrame, animationFrameNumber) =>
                    new AnimationYOffset(animationID, animationFrameNumber, FindLowHighPoints(OffsetYVector, Quaternion.Identity, animationFrame, animationFrame, 0f)))).ToList();
        }

        private List<AnimationYOffset> _animationYOffsets;
        private Vector3 _indicatorPoint;
        private float _offsetYLow;

        private float OffsetY { get; set; }

        private Vector3 OffsetYVector => new Vector3(0f, OffsetY, 0f);

        private void ExportFile()
        {
#if _WINDOWS && DEBUG
            try
            {
                const string target = @"d:\";
                if (!Directory.Exists(target)) return;
                DriveInfo[] drive = DriveInfo.GetDrives().Where(x =>
                    x.Name.IndexOf(Path.GetPathRoot(target), StringComparison.OrdinalIgnoreCase) >= 0).ToArray();
                DirectoryInfo di = new DirectoryInfo(target);
                if (!di.Attributes.HasFlag(FileAttributes.ReadOnly) && drive.Count() == 1 && drive[0].DriveType == DriveType.Fixed)
                    Extended.DumpBuffer(_buffer, Path.Combine(target, "out.dat"));
            }
            catch (IOException)
            {
            }
#endif
        }

        private void LoadFile(DebugBattleDat skeletonReference)
        {
            using (_br = new BinaryReader(new MemoryStream(_buffer)))
            {
                if (_br.BaseStream.Length - _br.BaseStream.Position < 4) return;
                switch (EntityType)
                {
                    case EntityType.Monster:
                        LoadMonster();
                        return;

                    case EntityType.Character:
                        LoadCharacter();
                        return;

                    case EntityType.Weapon:
                        LoadWeapon(skeletonReference);
                        return;

                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
        }

        private void LoadMonster()
        {
            if (ID == 127)
            {
                // per wiki 127 only have 7 & 8
                if (Flags.HasFlag(Sections.Information))
                    ReadSection7(DatFile[0]);
                //if (Flags.HasFlag(Sections.Scripts))
                //    ReadSection8(datFile.pSections[7]);
                return;
            }
            if (Flags.HasFlag(Sections.Skeleton))
                ReadSection1(DatFile[0]);
            if (Flags.HasFlag(Sections.ModelAnimation))
                ReadSection3(DatFile[2]); // animation data
            if (Flags.HasFlag(Sections.ModelGeometry))
                ReadSection2(DatFile[1]);

            //if (Flags.HasFlag(Sections.Section4_Unknown))
            //  ReadSection4(datFile.pSections[3]);
            if (Flags.HasFlag(Sections.AnimationSequences))
                ReadSection5(DatFile[4], DatFile[5]);
            //if (Flags.HasFlag(Sections.Section6_Unknown))
            //  ReadSection6(datFile.pSections[5]);
            if (Flags.HasFlag(Sections.Information))
                ReadSection7(DatFile[6]);
            //if (Flags.HasFlag(Sections.Scripts))
            //ReadSection8(datFile.pSections[7]); // battle scripts/ai

            //if (Flags.HasFlag(Sections.Sounds))
            //    ReadSection9(datFile.pSections[8], datFile.pSections[9]); //AKAO sounds
            //if (Flags.HasFlag(Sections.Sounds_Unknown))
            //  ReadSection10(datFile.pSections[9], datFile.pSections[10], br, fileName);

            if (Flags.HasFlag(Sections.Textures))
                ReadSection11(DatFile[10]);
        }

        private void LoadCharacter()
        {
            if (Flags.HasFlag(Sections.Skeleton))
                ReadSection1(DatFile[0]);
            if (Flags.HasFlag(Sections.ModelAnimation))
                ReadSection3(DatFile[2]);
            if (Flags.HasFlag(Sections.ModelGeometry))
                ReadSection2(DatFile[1]);
            if (ID == 7 && EntityType == EntityType.Character) // edna has no weapons.
            {
                if (Flags.HasFlag(Sections.Textures))
                    ReadSection11(DatFile[8]);
                if (Flags.HasFlag(Sections.AnimationSequences))
                    ReadSection5(DatFile[5], DatFile[6]);
            }
            else if (Flags.HasFlag(Sections.Textures))
                ReadSection11(DatFile[5]);
        }

        private void LoadWeapon(DebugBattleDat skeletonReference)
        {
            if (ID != 1 && ID != 9)
            {
                if (Flags.HasFlag(Sections.Skeleton))
                    ReadSection1(DatFile[0]);
                if (Flags.HasFlag(Sections.ModelAnimation))
                    ReadSection3(DatFile[2]);
                if (Flags.HasFlag(Sections.ModelGeometry))
                    ReadSection2(DatFile[1]);
                if (Flags.HasFlag(Sections.AnimationSequences))
                    ReadSection5(DatFile[3], DatFile[4]);
                if (Flags.HasFlag(Sections.Textures))
                    ReadSection11(DatFile[6]);
            }
            else if (skeletonReference != null)
            {
                Skeleton = skeletonReference.Skeleton;
                Animations = skeletonReference.Animations;
                if (Flags.HasFlag(Sections.ModelGeometry))
                    ReadSection2(DatFile[0]);
                if (Flags.HasFlag(Sections.AnimationSequences))
                    ReadSection5(DatFile[1], DatFile[2]);
                if (Flags.HasFlag(Sections.Textures))
                    ReadSection11(DatFile[4]);
            }
        }

        public IReadOnlyList<AnimationSequence> Sequences { get; set; }

        /// <summary>
        /// Animation Sequences
        /// </summary>
        /// <param name="start"></param>
        /// <param name="end"></param>
        /// <param name="br"></param>
        /// <param name="fileName"></param>
        /// <see cref="http://forums.qhimm.com/index.php?topic=19362.msg270092"/>
        private void ReadSection5(uint start, uint end)
        {
            Sequences = AnimationSequence.CreateInstances(_br, start, end);
        }

        public int GetId => ID;

        public int AltID { get; }
        public int ID { get; }
        public EntityType EntityType { get; }
        public string FileName { get; }
        public Sections Flags { get; }
    }
}