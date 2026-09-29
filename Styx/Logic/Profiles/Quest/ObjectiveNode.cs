using System;
using System.Xml.Linq;

#nullable disable
namespace Styx.Logic.Profiles.Quest
{
    public class ObjectiveNode : OrderNode
    {
        public ObjectiveNode(
            uint questId,
            ObjectiveType objectiveType,
            uint objectiveId,
            string objectiveName,
            int objectiveCount,
            int objectiveIndex)
            : base(OrderNodeType.Objective)
        {
            this.QuestId = questId;
            this.ObjectiveType = objectiveType;
            this.ObjectiveId = objectiveId;
            this.ObjectiveName = objectiveName;
            this.ObjectiveCount = objectiveCount;
            this.ObjectiveIndex = objectiveIndex;
        }

        public uint QuestId { get; private set; }

        public ObjectiveType ObjectiveType { get; private set; }

        public uint ObjectiveId { get; private set; }

        public string ObjectiveName { get; private set; }

        public int ObjectiveCount { get; private set; }

        public int ObjectiveIndex { get; private set; }

        public override string ToString()
        {
            return $"[ObjectiveNode QuestId: {QuestId}, Type: {ObjectiveType}, Id: {ObjectiveId}, Name: {ObjectiveName ?? "(null)"}, Count: {ObjectiveCount}, Index: {ObjectiveIndex}]";
        }

        public static ObjectiveNode FromXml(XElement element)
        {
            var questIdAttr = GetAttributeByAliases(element, "QuestId", "Quest");
            if (questIdAttr == null)
                throw new ProfileMissingAttributeException<int>("QuestId", element);
            if (!uint.TryParse(questIdAttr.Value, out uint questId) || questId == 0)
                throw new ProfileAttributeExpectedException<int>(questIdAttr);

            var typeAttr = GetAttributeByAliases(element, "Type");
            if (typeAttr == null)
                throw new ProfileMissingAttributeException("Type", element);
            if (!ObjectiveInfo.TryParseObjectiveType(typeAttr.Value, out ObjectiveType objectiveType)
                || objectiveType == ObjectiveType.TurnIn)
                throw new ProfileAttributeExpectedException(typeAttr);

            // Get objective ID using type-specific aliases (HB 4.3.4: smethod_2)
            string[] idAliases = GetIdAliases(objectiveType);
            var objectiveIdAttr = GetAttributeByAliases(element, idAliases);
            uint objectiveId = 0;
            if (objectiveIdAttr != null && (!uint.TryParse(objectiveIdAttr.Value, out objectiveId) || objectiveId == 0))
                throw new ProfileAttributeExpectedException<int>(objectiveIdAttr);

            string objectiveName = GetAttributeByAliases(element, "Name")?.Value;

            // Get count using type-specific aliases (HB 4.3.4: smethod_4)
            string[] countAliases = GetCountAliases(objectiveType);
            var countAttr = GetAttributeByAliases(element, countAliases);
            int objectiveCount = 1;
            if (countAttr != null && (!int.TryParse(countAttr.Value, out objectiveCount) || objectiveCount <= 0))
                throw new ProfileAttributeExpectedException<int>(countAttr);

            var indexAttr = GetAttributeByAliases(element, "Index");
            int objectiveIndex = -1;  // -1 = not specified, will search by ID instead
            if (indexAttr != null && (!int.TryParse(indexAttr.Value, out objectiveIndex) || objectiveIndex < -1))
                throw new ProfileAttributeExpectedException<int>(indexAttr);
            // Retain the existing explicit-index profile contract. The executor
            // resolves it against current quest objectives; absence of both forms
            // of identity must not turn into an arbitrary kill/collect instruction.
            if (objectiveIdAttr == null && objectiveIndex < 0)
                throw new ProfileMissingAttributeException<int>(idAliases[0], element);

            return new ObjectiveNode(questId, objectiveType, objectiveId, objectiveName, objectiveCount, objectiveIndex);
        }

        /// <summary>
        /// Gets ID attribute aliases based on objective type.
        /// HB 4.3.4: private static string[] smethod_2(ObjectiveType)
        /// </summary>
        private static string[] GetIdAliases(ObjectiveType objectiveType)
        {
            switch (objectiveType)
            {
                case ObjectiveType.KillMob:
                    return new[] { "Id", "Entry", "MobId", "MobEntry" };
                case ObjectiveType.CollectItem:
                    return new[] { "Id", "Entry", "ItemId", "ItemEntry" };
                case ObjectiveType.UseObject:
                    return new[] { "Id", "Entry", "GameObjectId", "ObjectId", "GameObjectEntry", "ObjectEntry", "UseGameObject", "UseObject" };
                default:
                    return new[] { "Id" };
            }
        }

        /// <summary>
        /// Gets count attribute aliases based on objective type.
        /// HB 4.3.4: private static string[] smethod_4(ObjectiveType)
        /// </summary>
        private static string[] GetCountAliases(ObjectiveType objectiveType)
        {
            switch (objectiveType)
            {
                case ObjectiveType.KillMob:
                    return new[] { "Count", "KillCount", "SlayCount" };
                case ObjectiveType.CollectItem:
                    return new[] { "Count", "CollectCount" };
                case ObjectiveType.UseObject:
                    return new[] { "Count", "GameObjectCount", "ObjectCount", "UseCount" };
                default:
                    return new[] { "Count" };
            }
        }

    }
}
