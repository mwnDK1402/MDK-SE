using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using SpaceEngineers.Game.ModAPI.Ingame;
using System.Collections.Generic;
using System.Collections;
using System.Linq;
using System.Text;
using System;
using VRage.Collections;
using VRage.Game.Components;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI.Ingame.Utilities;
using VRage.Game.ModAPI.Ingame;
using VRage.Game.ObjectBuilders.Definitions;
using VRage.Game;
using VRage;
using VRageMath;

namespace IngameScript
{
    partial class Program : MyGridProgram
    {
        private MyIni ini = new MyIni();
        private MyCommandLine commandLine = new MyCommandLine();

        private const string
            InfoSection = "InfoDisplay",
            AirlockSection = "Airlock";

        private readonly Dictionary<string, Action> commandByArgument;
        
        private List<IMyCockpit> displayCockpits;

        private IMyAirVent airlockVent;
        private List<IMyAirtightHangarDoor> exteriorDoors, interiorDoors, allDoors;

        private readonly MyIniKey
            airVentTagKey = new MyIniKey(AirlockSection, "AirVentTag"),
            exteriorAirlockTagKey = new MyIniKey(AirlockSection, "ExteriorDoorsTag"),
            interiorAirlockTagKey = new MyIniKey(AirlockSection, "InteriorDoorsTag");

        private IEnumerator coroutine;
        private int currentSensorDetections = 0;

        public Program()
        {
            this.commandByArgument = new Dictionary<string, Action>()
            {
                { "reset", this.Reset },
                { "interior_enter", () => this.OnInteriorSensorEnter() },
                { "interior_exit", () => this.OnInteriorSensorExit() },
                { "airlock_enter", () => this.StartCoroutine(this.OnAirlockSensorEnter()) },
                { "airlock_exit", () => this.OnAirlockSensorExit() },
                { "exterior_enter", () => this.OnExteriorSensorEnter() },
                { "exterior_exit", () => this.OnExteriorSensorExit() }
            };
            
            this.displayCockpits = new List<IMyCockpit>();
            this.exteriorDoors = new List<IMyAirtightHangarDoor>();
            this.interiorDoors = new List<IMyAirtightHangarDoor>();
            this.allDoors = new List<IMyAirtightHangarDoor>();

            this.Reset();
        }

        private void StartCoroutine(IEnumerator coroutine)
        {
            this.Echo($"Starting {coroutine.ToString()}");
            this.coroutine = coroutine;
            this.Runtime.UpdateFrequency |= UpdateFrequency.Once;
        }

        public void Save()
        {
        }

        public void Main(string argument, UpdateType updateSource)
        {
            this.RunCommand(argument);

            if ((updateSource & UpdateType.Once) == UpdateType.Once)
            {
                this.RunCoroutine();
            }
        }
        
        public void RunCoroutine()
        {
            if (this.coroutine != null)
            {
                bool hasMoreSteps = this.coroutine.MoveNext();
                
                if (hasMoreSteps)
                {
                    this.Runtime.UpdateFrequency |= UpdateFrequency.Once;
                }
                else
                {
                    (this.coroutine as IDisposable)?.Dispose();
                    this.coroutine = null;
                }
            }
        }

        private void RunCommand(string argument)
        {
            if (this.commandLine.TryParse(argument) && this.commandLine.ArgumentCount > 0)
            {
                string arg = this.commandLine.Argument(0).ToLower();
                Action command;
                if (this.commandByArgument.TryGetValue(arg, out command))
                    command();
                else
                    this.Echo($"Invalid Argument: {arg}");
            }
        }

        private void Reset()
        {
            // Programmable Block configuration
            MyIniParseResult result;
            if (!this.ini.TryParse(this.Me.CustomData, out result))
                throw new Exception(result.ToString());

            string airVentTag, exteriorAirlockTag, interiorAirlockTag;

            if (!this.ini.Get(this.airVentTagKey).TryGetString(out airVentTag))
                throw new Exception($"Key {this.airVentTagKey.Name} is required in Custom Data");

            if (!this.ini.Get(this.exteriorAirlockTagKey).TryGetString(out exteriorAirlockTag))
                throw new Exception($"Key {this.exteriorAirlockTagKey.Name} is required in Custom Data");

            if (!this.ini.Get(this.interiorAirlockTagKey).TryGetString(out interiorAirlockTag))
                throw new Exception($"Key {this.interiorAirlockTagKey.Name} is required in Custom Data");
            
            // Info displays
            this.displayCockpits.Clear();
            this.GridTerminalSystem.GetBlocksOfType(
                this.displayCockpits,
                c => this.IsDisplayCockpit(c));

            foreach (var cockpit in this.displayCockpits)
            {
                this.Echo($"{cockpit.CustomName} has {cockpit.SurfaceCount} surface(s)");
                for (int i = 0; i < cockpit.SurfaceCount; i++)
                {
                    var surface = cockpit.GetSurface(i);
                    this.Echo(surface.Name);
                }
            }

            // Hangar doors
            var doorGroups = new List<IMyBlockGroup>();
            this.GridTerminalSystem.GetBlockGroups(
                doorGroups,
                group => group.Name.Contains(exteriorAirlockTag));

            var exteriorDoorGroup = doorGroups.SingleOrDefault();
            if (exteriorDoorGroup != null)
                exteriorDoorGroup.GetBlocksOfType(this.exteriorDoors);
            else
                throw new Exception($"A single exterior airlock hangar is required. Its name must contain {exteriorAirlockTag}");

            this.GridTerminalSystem.GetBlockGroups(
                doorGroups,
                group => group.Name.Contains(interiorAirlockTag));

            var interiorDoorGroup = doorGroups.SingleOrDefault();
            if (interiorDoorGroup != null)
                interiorDoorGroup.GetBlocksOfType(this.interiorDoors);
            else
                throw new Exception($"A single interior airlock hangar is required. Its name must contain {interiorAirlockTag}");

            this.allDoors.AddRange(this.exteriorDoors);
            this.allDoors.AddRange(this.interiorDoors);

            // Air vents
            var airVents = new List<IMyAirVent>();
            this.GridTerminalSystem.GetBlocksOfType(
                airVents,
                v => v.CustomName.Contains(airVentTag));

            this.airlockVent = airVents.SingleOrDefault();
            if (this.airlockVent == null)
                throw new Exception($"A single exterior airlock hangar is required. Its name must contain {airVentTag}");

            this.Echo($"Reset: Found {this.displayCockpits.Count} info panels");
        }

        private bool IsDisplayCockpit(IMyCockpit cockpit) => this.HasConfigSection(cockpit, InfoSection);

        private bool IsAirlockDoor(IMyAirtightHangarDoor door) => this.HasConfigSection(door, AirlockSection);

        private bool HasConfigSection(IMyTerminalBlock block, string requiredSection) =>
            block.IsSameConstructAs(this.Me)
            && this.ini.TryParse(block.CustomData, requiredSection)
            && this.ini.ContainsSection(InfoSection);
        
        private IEnumerator ToggleDoorTwice()
        {
            var doorToCheck = this.interiorDoors[0];
            float prevOpenRatio = doorToCheck.OpenRatio;
            yield return null;
            float currOpenRatio = doorToCheck.OpenRatio;
            float openRatioDelta = currOpenRatio - prevOpenRatio;

            bool doorIsMoving = openRatioDelta != 0f;
            
            foreach (var door in this.interiorDoors)
                door.ToggleDoor();

            bool isDoorOpening = openRatioDelta < 0f || currOpenRatio == 0f;
            
            yield return null;

            while (doorToCheck.OpenRatio != (isDoorOpening ? 1f : 0f))
                yield return null;

            foreach (var door in this.interiorDoors)
                door.ToggleDoor();
        }

        private void OnInteriorSensorEnter()
        {
            if (this.currentSensorDetections++ > 0)
                return;

            this.StartCoroutine(this.OpenInteriorDoors());
        }

        private IEnumerator OpenInteriorDoors()
        {
            this.airlockVent.Depressurize = false;
            while (this.airlockVent.GetOxygenLevel() < 1f)
                yield return null;

            foreach (var door in this.interiorDoors)
                door.OpenDoor();
        }

        private void OnInteriorSensorExit()
        {
            this.currentSensorDetections--;

            foreach (var door in this.interiorDoors)
                door.CloseDoor();
        }

        private IEnumerator OnAirlockSensorEnter()
        {
            this.currentSensorDetections++;

            bool allDoorsClosed = false;
            while (!allDoorsClosed)
            {
                yield return null;
                allDoorsClosed = true;
                foreach (var door in this.allDoors)
                    if (door.OpenRatio > 0f)
                    {
                        allDoorsClosed = false;
                        break;
                    }
            }
            
            if (this.airlockVent.GetOxygenLevel() > 0.5f)
            {
                this.airlockVent.Depressurize = true;
                while (this.airlockVent.GetOxygenLevel() > 0f)
                    yield return null;

                foreach (var door in this.exteriorDoors)
                    door.OpenDoor();
            }
            else
            {
                this.airlockVent.Depressurize = false;
                while (this.airlockVent.GetOxygenLevel() < 1f)
                    yield return null;

                foreach (var door in this.interiorDoors)
                    door.OpenDoor();
            }
        }

        private void OnAirlockSensorExit()
        {
            this.currentSensorDetections--;
        }

        private void OnExteriorSensorEnter()
        {
            if (this.currentSensorDetections++ > 0)
                return;

            this.StartCoroutine(this.OpenExteriorDoors());
        }

        private IEnumerator OpenExteriorDoors()
        {
            this.airlockVent.Depressurize = true;
            while (this.airlockVent.GetOxygenLevel() > 0f)
                yield return null;

            foreach (var door in this.exteriorDoors)
                door.OpenDoor();
        }

        private void OnExteriorSensorExit()
        {
            this.currentSensorDetections--;

            foreach (var door in this.exteriorDoors)
                door.CloseDoor();
        }
    }
}
