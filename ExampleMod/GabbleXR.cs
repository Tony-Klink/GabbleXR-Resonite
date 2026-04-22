using System.Threading;
using System.Collections.Generic;
using Elements.Core;
using FrooxEngine;
using HarmonyLib;
using ResoniteModLoader;
using Rug.Osc;

namespace GabbleXR;

public class GabbleXR : ResoniteMod {
	internal const string VERSION_CONSTANT = "1.0.0";
	public override string Name => "GabbleXR";
	public override string Author => "Klink";
	public override string Version => VERSION_CONSTANT;
	public override string Link => "https://github.com/Tony-Klink/GabbleXR/";

	[AutoRegisterConfigKey]
	private static readonly ModConfigurationKey<bool> ModEnabledKey = new("Enabled", "Mod Enabled", () => true);

	[AutoRegisterConfigKey]
	private static readonly ModConfigurationKey<int> OscPortKey = new("OscPort", "OSC Port", () => 9000);

	private static ModConfiguration? _config;
	private static OscManager? _oscManager;
	private static GabbleXRInterface? _interface;

	public override void OnEngineInit() {
		_config = GetConfiguration();
		
		new Harmony("com.klink.GabbleXR").PatchAll();

		Engine.Current.RunPostInit(() => {
			try {
				_interface = new GabbleXRInterface();
				Engine.Current.InputInterface.RegisterInputDriver(_interface);
				
				int port = _config?.GetValue(OscPortKey) ?? 9000;
				_oscManager = new OscManager(port, _interface);
				_oscManager.Start();
				
				Msg($"GabbleXR initialized on port {port}");
			} catch (Exception e) {
				Error($"Failed to initialize GabbleXR: {e}");
			}
		});
	}

	private class OscManager(int port, GabbleXRInterface @interface) {
		private OscReceiver? _receiver;
		private Thread? _thread;
		private bool _isRunning;

		public void Start() {
			_receiver = new OscReceiver(port);
			_isRunning = true;
			_thread = new Thread(ListenLoop) { IsBackground = true, Name = "GabbleXR OSC Listener" };
			_thread.Start();
			_receiver.Connect();
		}

		private void ListenLoop() {
			while (_isRunning && _receiver != null) {
				try {
					if (_receiver.State != OscSocketState.Connected) {
						Thread.Sleep(100);
						continue;
					}

					if (_receiver.Receive() is OscMessage message) {
						ProcessMessage(message);
					}
				} catch (Exception e) when (_receiver?.State != OscSocketState.Closed) {
					Error($"OSC Loop Error: {e.Message}");
				}
			}
		}

		private void ProcessMessage(OscMessage message) {
			if (message.Count == 0) return;

			float[] values = new float[message.Count];
			for (int i = 0; i < message.Count; i++) {
				if (message[i] is float f) values[i] = f;
				else if (message[i] is int val) values[i] = val;
			}

			// Strip leading slash and handle nested paths
			string path = message.Address.TrimStart('/');
			@interface.SetTrackingValues(path, values);
		}

		public void Stop() {
			_isRunning = false;
			_receiver?.Close();
		}
	}

	private class GabbleXRInterface : IInputDriver {
		public int UpdateOrder => 100;
		private InputInterface _input = null!;
		private Eyes? _eyes;
		private Mouth? _mouth;

		private bool _enabled;
        private uint _lastUpdate;


		private readonly Dictionary<string, float[]> _trackingValues = new(StringComparer.OrdinalIgnoreCase);

		public void SetTrackingValues(string path, float[] values) {
			lock (_trackingValues) {
				_trackingValues[path] = values;
			}
		}

		private float GetValue(string path, int index = 0) {
			lock (_trackingValues) {
				if (_trackingValues.TryGetValue(path, out float[]? values) && index < values.Length) {
					return values[index];
				}
				return 0f;
			}
		}

		public void CollectDeviceInfos(DataTreeList list) {
			DataTreeDictionary dict = new();
			dict.Add("Name", "GabbleXR Tracking");
			dict.Add("Type", "Tracking");
			dict.Add("Model", "GabbleXR");
			list.Add(dict);
		}

		public void RegisterInputs(InputInterface inputInterface) {
			_input = inputInterface;
			// _enabled = _config.GetValue(ModEnabledKey);
			_eyes = new Eyes(inputInterface, "GabbleXR Eyes", false);
			var groups = (MouthParameterGroup[])Enum.GetValues(typeof(MouthParameterGroup));
			_mouth = new Mouth(inputInterface, "GabbleXR Mouth", groups);
		}

		public void UpdateInputs(float deltaTime) {
			if (_eyes == null || _mouth == null) return;

			UpdateEyes(deltaTime);
			UpdateMouth();
		}

		private void UpdateEyes(float deltaTime) {
			if (_eyes == null) return;
			// if (!_enabled || _lastUpdate > 10) {
            //     _eyes.IsEyeTrackingActive = false;
            //     _eyes.SetTracking(false);
            //     return;
			// }

			_eyes.Timestamp += deltaTime;
			_eyes.IsEyeTrackingActive = _input.VR_Active;
            _eyes.SetTracking(true);

			_eyes.IsDeviceActive = true;

			UpdateEye(_eyes.LeftEye, "eye/Left");
			UpdateEye(_eyes.RightEye, "eye/Right");

			_eyes.ComputeCombinedEyeParameters();
			_eyes.ConvergenceDistance = 0f;
			_eyes.FinishUpdate();
		}

		private void UpdateEye(Eye eye, string prefix) {
			eye.IsDeviceActive = true;
			eye.IsTracking = true;
			
			// Direction (fff: horizontal, vertical, unknown)
			// float3 direction = new float3(GetValue($"{prefix}/Direction", 0), GetValue($"{prefix}/Direction", 1), 0f);
			float x = GetValue($"{prefix}/Direction", 0);
			float y = GetValue($"{prefix}/Direction", 1);
			float z = MathX.Sqrt(1f - (x * x + y * y));
			float3 dir = new float3(x, y, z).Normalized;
			eye.UpdateWithDirection(dir);

			eye.Openness = GetValue($"{prefix}/Openness");
			eye.Widen = GetValue($"{prefix}/Widen");
			eye.Squeeze = GetValue($"{prefix}/Squeeze");
			eye.Frown = GetValue($"{prefix}/Frown");

			eye.InnerBrowVertical = GetValue($"{prefix}/InnerBrowVertical");
			eye.OuterBrowVertical = GetValue($"{prefix}/OuterBrowVertical");
		}

		private void UpdateMouth() {
			if (_mouth == null) return;

			_mouth.IsTracking = true;
			_mouth.IsDeviceActive = true;

			// Jaw
			_mouth.JawOpen = GetValue("mouth/JawOpen");
			_mouth.Jaw = new float3(GetValue("mouth/Jaw", 0), GetValue("mouth/Jaw", 1), GetValue("mouth/Jaw", 2));

			// Tongue (fff: sideways, vertical, out)
			_mouth.Tongue = new float3(GetValue("mouth/Tongue", 0), GetValue("mouth/Tongue", 1),GetValue("mouth/Tongue", 2));
			_mouth.TongueRoll = GetValue("/mouth/TongueRoll");

			// Lips
			_mouth.LipUpperLeftRaise = GetValue("/mouth/LipUpperLeftRaise");
			_mouth.LipUpperRightRaise = GetValue("/mouth/LipUpperRightRaise");
			_mouth.LipLowerLeftRaise = GetValue("/mouth/LipLowerLeftRaise");
			_mouth.LipLowerRightRaise = GetValue("/mouth/LipLowerRightRaise");
			_mouth.LipUpperHorizontal = GetValue("/mouth/LipUpperHorizontal");
			_mouth.LipLowerHorizontal = GetValue("/mouth/LipLowerHorizontal");
			_mouth.LipTopLeftOverturn = GetValue("mouth/LipTopLeftOverturn");
			_mouth.LipTopRightOverturn = GetValue("mouth/LipTopRightOverturn");
			_mouth.LipBottomLeftOverturn = GetValue("mouth/LipBottomLeftOverturn");
			_mouth.LipBottomRightOverturn = GetValue("mouth/LipBottomRightOverturn");
			_mouth.LipTopLeftOverUnder = GetValue("mouth/LipTopLeftOverUnder");
			_mouth.LipTopRightOverUnder = GetValue("mouth/LipTopRightOverUnder");
			_mouth.LipBottomLeftOverUnder = GetValue("mouth/LipBottomLeftOverUnder");
			_mouth.LipBottomRightOverUnder = GetValue("mouth/LipBottomRightOverUnder");
			_mouth.LipLeftStretchTighten = GetValue("mouth/LipLeftStretchTighten");
			_mouth.LipRightStretchTighten = GetValue("mouth/LipRightStretchTighten");
			_mouth.LipsLeftPress = GetValue("mouth/LipsLeftPress");
			_mouth.LipsRightPress = GetValue("mouth/LipsRightPress");

			// Mouth
			_mouth.MouthLeftSmileFrown = GetValue("/mouth/MouthLeftSmileFrown");
			_mouth.MouthRightSmileFrown = GetValue("/mouth/MouthRightSmileFrown");
			_mouth.MouthLeftDimple = GetValue("/mouth/MouthLeftDimple");
			_mouth.MouthRightDimple = GetValue("/mouth/MouthRightDimple");
			_mouth.MouthPoutLeft = GetValue("mouth/MouthPoutLeft");
			_mouth.MouthPoutRight = GetValue("mouth/MouthPoutRight");

			// Cheeks
			_mouth.CheekLeftPuffSuck = GetValue("mouth/CheekLeftPuffSuck");
			_mouth.CheekRightPuffSuck = GetValue("mouth/CheekRightPuffSuck");
			_mouth.CheekLeftRaise = GetValue("mouth/CheekLeftRaise");
			_mouth.CheekRightRaise = GetValue("mouth/CheekRightRaise");

			// Nose
			_mouth.NoseWrinkleLeft = GetValue("mouth/NoseWrinkleLeft");
			_mouth.NoseWrinkleRight = GetValue("mouth/NoseWrinkleRight");

			// Chin
			_mouth.ChinRaiseBottom = GetValue("mouth/ChinRaiseBottom");
			_mouth.ChinRaiseTop = GetValue("mouth/ChinRaiseTop");
		}
	}
}
