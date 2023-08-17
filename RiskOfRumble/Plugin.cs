using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using Buttplug.Client;
using Buttplug.Client.Connectors.WebsocketConnector;
using RoR2;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using UnityEngine;

namespace RiskOfRumble
{
    [BepInPlugin(ID, NAME, VERSION)]
    public class RiskOfRumble : BaseUnityPlugin
    {
        public const string ID = "com.exp111.RiskOfRumble";
        public const string NAME = "RiskOfRumble";
        public const string VERSION = "1.0";

        public static ManualLogSource Log;
        private static ButtplugClient ButtplugClient;

        public static ConfigEntry<string> ServerURL;

        public static ConfigEntry<bool> Rewarding;
        public static ConfigEntry<bool> Punishing;
        public static ConfigEntry<float> RewardFactor;
        public static ConfigEntry<float> PunishmentFactor;

        public void Awake()
        {
            try
            {
                Log = Logger;
                Log.LogMessage("Awake");
                DebugLog("Using a DEBUG build.");
                SetupConfig();

                // Init Bp
                ButtplugClient = new ButtplugClient("RiskOfRumble");
                ButtplugClient.DeviceAdded += ButtplugClient_DeviceAdded;
                ButtplugClient.PingTimeout += ButtplugClient_PingTimeout;
                ButtplugClient.ErrorReceived += ButtplugClient_ErrorReceived;
                Connect();
                GlobalEventManager.onClientDamageNotified += GlobalEventManager_onClientDamageNotified;
            }
            catch (Exception e)
            {
                Log.LogMessage($"Exception during RiskOfRumble.Awake: {e}");
            }
        }

        private void ButtplugClient_DeviceAdded(object sender, DeviceAddedEventArgs e)
        {
            Log.LogMessage($"Device added from RiskOfRumble.ButtplugClient: {e}");
        }

        private void ButtplugClient_PingTimeout(object sender, EventArgs e)
        {
            Log.LogMessage($"Ping Timeout from RiskOfRumble.ButtplugClient: {e}");
        }

        private void ButtplugClient_ErrorReceived(object sender, Buttplug.Core.ButtplugExceptionEventArgs e)
        {
            Log.LogMessage($"Error received from RiskOfRumble.ButtplugClient: {e}");
        }

        private void SetupConfig()
        {
            ServerURL = Config.Bind("Connection", "URL", "ws://localhost:12345", "The server url which to join");
            Config.Bind("Connection", "Connect", false, new ConfigDescription("Scan/reconnect",
                null,
                new ConfigurationManagerAttributes { CustomDrawer = ConnectDrawer, HideDefaultButton = true }));

            Rewarding = Config.Bind("Gameplay", "Rewarding", true, "If rewarding actions (hitting things) should trigger vibrations");
            Punishing = Config.Bind("Gameplay", "Punishing", false, "If punishing actions (getting hit) should trigger vibrations");
            RewardFactor = Config.Bind("Gameplay", "Reward Factor", 0.5f, "The reward multiplier");
            PunishmentFactor = Config.Bind("Gameplay", "Punishment Factor", 2f, "The punishment multiplier");
        }

        private void Connect()
        {
            try
            {
                DebugLog($"Connecting to {ServerURL.Value}");
                var connector = new ButtplugWebsocketConnector(new Uri(ServerURL.Value));
                ButtplugClient.ConnectAsync(connector).Wait();
            }
            catch (Exception e)
            {
                Log.LogMessage($"Exception during RiskOfRumble.Connect: {e}");
            }
        }

        private void Disconnect()
        {
            try
            {
                DebugLog($"Disconnecting");
                ButtplugClient.DisconnectAsync();
            }
            catch (Exception e)
            {
                Log.LogMessage($"Exception during RiskOfRumble.Disconnect: {e}");
            }
        }

        public void ConnectDrawer(ConfigEntryBase entry)
        {
            try
            {
                //TODO: connection status + devices
                GUILayout.TextArea($"Status: {(ButtplugClient.Connected ? "Connected" : "Disconnected")}", GUILayout.ExpandWidth(true));
                GUILayout.TextArea($"#Devices: {ButtplugClient.Devices.Length}", GUILayout.ExpandWidth(true));
                // Buttons //TODO: disable/hide if connected/disconnected
                if (GUILayout.Button("Connect", GUILayout.ExpandWidth(true)))
                {
                    DebugLog("Connecting");
                    Connect();
                }
                if (GUILayout.Button("Scan", GUILayout.ExpandWidth(true)))
                {
                    DebugLog("Scanning");
                    ButtplugClient.StartScanningAsync();
                }
                if (GUILayout.Button("Disconnect", GUILayout.ExpandWidth(true)))
                {
                    DebugLog($"Disconnecting");
                    Disconnect();
                }
            }
            catch (Exception e)
            {
                Log.LogMessage($"Exception during RiskOfRumble.CustomDrawer: {e}");
            }
        }

        public class Vibration
        {
            public float Intensity;
            public Vibration(float intensity)
            {
                Intensity = intensity;
            }
        }
        public static List<Vibration> Vibrations = new List<Vibration>();
        public static void Vibrate(float strength)
        {
            var str = Math.Max(0, Math.Min(1f, strength));
            Vibrations.Add(new Vibration(str));
            SendVibrate(str);
        }

        public void Update()
        {
            try
            {
                var deltaTime = Time.deltaTime;
                if (Vibrations.Count == 0)
                {
                    return;
                }

                // Fade the vibration out
                foreach (var t in Vibrations)
                {
                    t.Intensity = Mathf.Lerp(t.Intensity, 0, 0.5f * deltaTime);
                }

                // Remove all done vibrations
                Vibrations.RemoveAll(p => p.Intensity <= 0f);

                // Vibration this tick is the sum of all current vibrations
                var sum = Vibrations.Sum(item => item.Intensity);

                SendVibrate(sum);
            }
            catch (Exception e)
            {
                Log.LogMessage($"Exception during RiskOfRumble.Update: {e}");
            }
        }

        public static void SendVibrate(float strength)
        {
            //TODO: specify which device
            foreach (var device in ButtplugClient.Devices)
            {
                device.VibrateAsync(strength);
            }
        }

        [Conditional("DEBUG")]
        public static void DebugLog(string message)
        {
            Log.LogMessage(message);
        }

        [Conditional("TRACE")]
        public static void DebugTrace(string message)
        {
            Log.LogMessage(message);
        }

        private void GlobalEventManager_onClientDamageNotified(DamageDealtMessage message)
        {
            try
            {
                if (!message.victim)
                {
                    return;
                }

                if (Rewarding.Value)
                {
                    Reward(message);
                }

                if (Punishing.Value)
                {
                    Punish(message);
                }
            }
            catch (Exception e)
            {
                Log.LogMessage($"Exception during GlobalEventManager.onClientDamageNotified hook: {e}");
            }
        }

        private void Punish(DamageDealtMessage obj)
        {
            var playermaster = PlayerCharacterMasterController.instances[0].master;
            if (!playermaster)
            {
                return;
            }
            var playerbody = playermaster.GetBody();
            if (!playerbody)
            {
                return;
            }

            var victim = obj.victim;

            if (victim != playerbody.gameObject)
            {
                return;
            }

            var healthcomp = victim.GetComponent<HealthComponent>();
            Vibrate(PunishmentFactor.Value * (obj.damage / healthcomp.fullCombinedHealth));
        }

        private void Reward(DamageDealtMessage obj)
        {
            var playermaster = PlayerCharacterMasterController.instances[0].master;
            if (!playermaster)
            {
                return;
            }

            var playerbody = playermaster.GetBody();
            if (!playerbody)
            {
                return;
            }

            if (obj.attacker != playerbody.gameObject)
            {
                return;
            }

            var victim = obj.victim;
            if (!victim)
            {
                return;
            }

            var healthcomp = victim.GetComponent<HealthComponent>();
            Vibrate(RewardFactor.Value * (obj.damage / healthcomp.fullCombinedHealth));
        }
    }
}
