using System.Collections.Generic;

namespace IngameScript
{
    /// <summary>Curated map of item subtypes whose blueprint subtype differs from the item subtype. Covers vanilla mismatches where naive <c>MyDefinitionId("MyObjectBuilder_BlueprintDefinition/" + itemSubtype)</c> fails (e.g. <c>Construction</c> → <c>ConstructionComponent</c>).</summary>
    public static class BlueprintMisses
    {
        /// <summary>Item subtype → blueprint subtype map for vanilla components whose blueprint names diverge.</summary>
        public static readonly Dictionary<string, string> CuratedMap = new Dictionary<string, string> {
            { "Construction", "ConstructionComponent" },
            { "Computer", "ComputerComponent" },
            { "Detector", "DetectorComponent" },
            { "Explosives", "ExplosivesComponent" },
            { "Girder", "GirderComponent" },
            { "Medical", "MedicalComponent" },
            { "Motor", "MotorComponent" },
            { "RadioCommunication", "RadioCommunicationComponent" },
            { "Reactor", "ReactorComponent" },
            { "Thrust", "ThrustComponent" }
        };
    }
}
