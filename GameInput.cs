using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
#if UNITY_SWITCH && !UNITY_EDITOR
using nn;
using nn.hid;
using nn.util;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.Switch;
#endif
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Composites;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;

public class GameInput : Singleton<GameInput>
{
    protected GameInput() { }

    private bool _initialized = false;

    private NWControls _controls;
    public NWControls Controls => _controls;

    private InputDevice m_LastUsedDevice = null;

    public InputDevice LastUsedDevice => m_LastUsedDevice;

    private float m_SensitivityX;
    private float m_SensitivityY;
    
#if UNITY_SWITCH && !UNITY_EDITOR
    private bool m_gyroEnabled = false;

    private bool GyroEnabled
    {
        get => m_gyroEnabled;
        set
        {
            if (m_gyroEnabled != value)
            {
                m_gyroEnabled = value;
                
                if (value)
                    StartSixAxisSensors();
                else
                    StopSixAxisSensors();
            }
        }
    }

    private GyroJoyConSide _gyroJoyCon = 0;

    private GyroJoyConSide GyroJoyCon
    {
        get => _gyroJoyCon;
        set
        {
            GyroJoyConSide availableValue = (GyroJoyConSide)((int) value % _availableGyroSources);
            if (_gyroJoyCon != value)
            {
                _gyroJoyCon = value;
                
                StopSixAxisSensors();
                StartSixAxisSensors();
            }
        }
    }

    public event Action OnGyroReset;

    private int _availableGyroSources = 1;
    private NpadId _chosenDeviceNpadId = NpadId.Invalid;

    private bool _hasShownGyroSafetyWarning = false;

    public bool HasShownGyroSafetyWarning
    {
        get => _hasShownGyroSafetyWarning;
        set => _hasShownGyroSafetyWarning = value;
    }
#endif

    private float m_baseSensitivityMultiplier = 1f;
    private float m_defaultInputDeviceChangeThreshold = 0.1f;
    [SerializeField]
    private float m_mouseInputDeviceChangeThreshold = 100.0f;
    public bool IsDeviceChangedToNonMouse { get; set; }

    private InputDevice m_connectedMouse;
    private InputDevice m_connectedKeyboard;
    private InputDevice m_connectedGamepad;

    private InputDevice ConnectedGamepad
    {
        get => m_connectedGamepad;
        set
        {
            if (m_connectedGamepad != value)
            {
#if UNITY_SWITCH && !UNITY_EDITOR
                if(m_gyroEnabled && m_connectedGamepad != null)
                    StopSixAxisSensors();
#endif

                m_connectedGamepad = value;

#if UNITY_SWITCH && !UNITY_EDITOR
                if(m_gyroEnabled && m_connectedGamepad != null)
                    StartSixAxisSensors();
#endif
            }
        }
    }

    public bool IsGamepadConnected => m_connectedGamepad != null;

    public bool IsReadyForGamepadSubmitHandling { get; set; }

    public enum InputDeviceClass
    {
        None,
        Keyboard,
        Mouse,
        Gamepad
    }
    
    public enum GameActions
    {
        Jump,
        FireCard,
        FireCardAlt,
        AutoAim,
        MoveHorizontal,
        MoveVertical,
        LookHorizontal,
        LookVertical,
        SwapCard,
        Start,
        Restart,
        Pause,
        Leaderboards,
        UISubmit,
        UICancel,
        DialogueFastForward,
        MenuTabRight,
        MenuTabLeft,
        DialogueAdvance,
#if UNITY_SWITCH && !UNITY_EDITOR
        GyroOrientation,
        GyroReset,
        GyroSwitchSource
#endif
    }

    public enum GyroAxis
    {
        Yaw = 0,
        Roll
    }

    public enum GyroZeroDriftMode
    {
        Loose,
        Standard,
        Tight
    }

    public enum GyroJoyConSide
    {
        Left = 0,
        Right
    }

    public void Initialize()
    {
        if (_initialized) return;
        
#if UNITY_SWITCH && !UNITY_EDITOR
        bool didShowApplet = TryShowNativeControllerApplet();
#endif
        _controls = new NWControls();
        _controls.Enable();

        GameInput.Instance.LoadPrefs();

        QueryAvailableInputDevices();
        
#if UNITY_SWITCH && !UNITY_EDITOR
        if(!didShowApplet && m_gyroEnabled)
        {
            StartSixAxisSensors();
        }
#endif        

        InputSystem.onEvent += OnInputSystemEvent;
        InputSystem.onDeviceChange += OnDeviceChange;
#if !UNITY_SWITCH || UNITY_EDITOR
        InputSystem.onActionChange += OnActionChange;
#endif
        lastUsedDeviceChangedEvent.AddListener(OnInputDeviceChanged);
        
        _initialized = true;
    }

    public InputDevice GetInputDeviceForDeviceClass(InputDeviceClass deviceClass)
    {
        switch (deviceClass)
        {
            case InputDeviceClass.Gamepad:
                return m_connectedGamepad;
            case InputDeviceClass.Keyboard:
                return m_connectedKeyboard;
            case InputDeviceClass.Mouse:
                return m_connectedMouse;
        }

        return null;
    }

    private void OnActionChange(System.Object o, InputActionChange change)
    {
        if (!GameConsole.GetIsConsoleOpen())
        {
            switch (change)
            {
                case InputActionChange.ActionStarted:
                    if (o is InputAction action)
                    {
                        if (GetDeviceClassFromControl(action.activeControl) == InputDeviceClass.Keyboard
                            && _controls.UI.Navigate.controls.Contains(action.activeControl)
                            && Keyboard.current != null
                            && m_LastUsedDevice != Keyboard.current)
                        {
                            m_LastUsedDevice = Keyboard.current;
                            lastUsedDeviceChangedEvent?.Invoke(m_LastUsedDevice);
                        }
                    }
                    break;
            }
        }
    }

#if UNITY_SWITCH && !UNITY_EDITOR
    private bool TryShowNativeControllerApplet()
    {
        if (NPad.all.Count != 1)
        {
            ControllerSupportArg controllerArg = new ControllerSupportArg();
            controllerArg.SetDefault();
            controllerArg.playerCountMax = 1;
            controllerArg.enableSingleMode = true;
            controllerArg.enableTakeOverConnection = false;

            ControllerSupportResultInfo resultInfo = new ControllerSupportResultInfo();

            UnityEngine.Switch.Applet.Begin();
            Result result = ControllerSupport.Show(ref resultInfo, controllerArg);
            UnityEngine.Switch.Applet.End();

            if (!result.IsSuccess())
            {
                Debug.LogErrorFormat("ShowNativeControllerApplet failed with the result: {0}", result);
                return false;
            }

            _chosenDeviceNpadId = resultInfo.selectedId;

            return true;
        }
        return false;
    }
#endif
    

    private void OnDeviceChange(InputDevice device, InputDeviceChange change)
    {
        switch (change)
        {
            case InputDeviceChange.Added:
#if UNITY_SWITCH && !UNITY_EDITOR
                TryShowNativeControllerApplet();
#endif
                break;

            case InputDeviceChange.Removed:
#if UNITY_SWITCH && !UNITY_EDITOR
                if (NPad.all.Count == 0)
                    TryShowNativeControllerApplet();
#endif
                break;

            case InputDeviceChange.ConfigurationChanged:
#if UNITY_SWITCH && !UNITY_EDITOR
                //This will essentially reset the six axis sensor as this configuration changed event means the current gamepad device has changed in some way,
                //the main one being if it's changed the source of its input (e.g. if you change from JoyCon to Pro Pad), why these don't count as unique devices
                //in unity input system, I do not know.
                ConnectedGamepad = null; 
#endif
                break;
        }
        
        QueryAvailableInputDevices();
    }

    private void QueryAvailableInputDevices()
    {
        List<InputDevice> devices = new List<InputDevice>();

#if UNITY_SWITCH && !UNITY_EDITOR
        foreach (var inputDevice in InputSystem.devices)
        {
            if (inputDevice is NPad npad)
            {
                if (_chosenDeviceNpadId == NpadId.Invalid || _chosenDeviceNpadId == (NpadId) npad.npadId)
                {
                    ConnectedGamepad = npad;
                    _chosenDeviceNpadId = (NpadId)npad.npadId;
                    devices.Add(npad);
                    break;
                }
            }
        }
#else
        foreach (var inputDevice in InputSystem.devices)
        {
            switch (inputDevice.name)
            {
                case "Mouse":
                    m_connectedMouse = inputDevice;
                    devices.Add(m_connectedMouse);
                    break;
                case "Keyboard":
                    m_connectedKeyboard = inputDevice;
                    devices.Add(m_connectedKeyboard);
                    break;
                case "Gamepad":
                    ConnectedGamepad = inputDevice;
                    devices.Add(ConnectedGamepad);
                    break;
            }
        }
        
        if (Gamepad.all.Count > 0)
        {
            ConnectedGamepad = Gamepad.all[0];
            foreach (var gamepad in Gamepad.all)
            {
                devices.Add(gamepad);
            }
        }
#endif

        if (devices.Count > 0)
        {
            _controls.devices = devices.ToArray();
        }
    }
    

    public void SetFirstAvailableInputDevice()
    {
        m_LastUsedDevice ??= m_connectedGamepad ?? (m_connectedMouse ?? m_connectedKeyboard);
        if (_controls.devices == null || _controls.devices.Value.Count == 0)
        {
            QueryAvailableInputDevices();
        }
        OnInputDeviceChanged(null);
    }
    

    void OnInputSystemEvent(InputEventPtr eventPtr, InputDevice device)
    {
        if (m_LastUsedDevice == device) return;

        var eventType = eventPtr.type;
        if (eventType == StateEvent.Type) {
            if (!eventPtr.EnumerateChangedControls(device, string.Equals(device.name, "Mouse", StringComparison.Ordinal) ? 
                m_mouseInputDeviceChangeThreshold : m_defaultInputDeviceChangeThreshold).Any()) return;
        }
        
        if (device is Keyboard deviceToSwitchTo)
        {
            return;
        }

        m_LastUsedDevice = device;
        lastUsedDeviceChangedEvent?.Invoke(device);
    }

    private void OnInputDeviceChanged(InputDevice inputDevice)
    {
        var lastUsedInputDevice = GetLastActiveDeviceClass();
        ApplySensitivityByInputDeviceClass(lastUsedInputDevice == InputDeviceClass.Gamepad ? InputDeviceClass.Gamepad : InputDeviceClass.Mouse);
        if (MainMenu.Instance() == null || MainMenu.Instance()?.GetCurrentState() == MainMenu.State.None) return;
        
        var selectedGameObject = EventSystem.current.currentSelectedGameObject;
        if (lastUsedInputDevice == InputDeviceClass.Mouse)
        {
            if (selectedGameObject != null)
            {
                var buttonBase = selectedGameObject.GetComponent<MenuButtonBase>();
                var button = selectedGameObject.GetComponent<Button>();
                if (buttonBase != null)
                {
                    buttonBase.OnPointerExit(null);
                }
                else if (button != null)
                {
                    button.OnPointerExit(null);
                }
                EventSystem.current.SetSelectedGameObject(null);
                IsReadyForGamepadSubmitHandling = false;
            }
            Game.Instance.SetCursorLock(false);
        }
        else
        {
            IsDeviceChangedToNonMouse = true;
        }
    }

    private void ApplySensitivityByInputDeviceClass(InputDeviceClass inputDeviceClass)
    {
        m_SensitivityX = inputDeviceClass == InputDeviceClass.Gamepad
            ? m_baseSensitivityMultiplier
            : GameDataManager.prefs.mouseSensitivity;

        m_SensitivityY = inputDeviceClass == InputDeviceClass.Gamepad
            ? m_baseSensitivityMultiplier
            : GameDataManager.prefs.mouseSensitivity;
    }

    public bool WasKeyboardArrowsPressed()
    {
        return GetLastActiveDeviceClass() == InputDeviceClass.Keyboard &&
               _controls.UI.Navigate.WasPressedThisFrame() ||
               _controls.UI.Navigate.WasReleasedThisFrame();
    }

    public bool WasKeyboardSubmitOrCancelPressed()
    {
        return GetLastActiveDeviceClass() == InputDeviceClass.Keyboard &&
               _controls.UI.Cancel.WasPressedThisFrame()  || 
               _controls.UI.Submit.WasPressedThisFrame()  ||
               _controls.UI.Cancel.WasReleasedThisFrame() ||
               _controls.UI.Submit.WasReleasedThisFrame();
    }
    
    public bool WasSubmitPressed()
    {
        return GetLastActiveDeviceClass() != InputDeviceClass.Mouse &&
               _controls.UI.Submit.WasPressedThisFrame()  ||
               _controls.UI.Submit.WasReleasedThisFrame();
    }
    
    

    public InputDeviceClass GetLastActiveDeviceClass()
    {
        if (m_LastUsedDevice == null)
        {
            if (m_connectedMouse != null) return InputDeviceClass.Mouse;
            if (m_connectedGamepad != null) return InputDeviceClass.Gamepad;

#if UNITY_SWITCH
            return InputDeviceClass.Gamepad;
#else
            return InputDeviceClass.None;
#endif
        }
        return GetDeviceClassFromPath(m_LastUsedDevice.name);
    }
    
    public InputDeviceClass GetDeviceClassFromPath(string devicePath)
    {
        if(!string.IsNullOrEmpty(devicePath))
        {
            switch (devicePath)
            {
                case "PC;Keyboard":
                case "Keyboard":
                    return InputDeviceClass.Keyboard;
                case "Mouse":
                    return InputDeviceClass.Mouse;
                case "Gamepad":
                    return InputDeviceClass.Gamepad;
                default:
                    if(m_LastUsedDevice != null)
                    {
                        if (CultureInfo.InvariantCulture.CompareInfo.IndexOf(m_LastUsedDevice.name, "pad", CompareOptions.IgnoreCase) >= 0 ||
                            CultureInfo.InvariantCulture.CompareInfo.IndexOf(m_LastUsedDevice.name, "controller", CompareOptions.IgnoreCase) >= 0)
                        {
                            return InputDeviceClass.Gamepad;
                        }
                    }

                    break;
            }
        }
        
        return InputDeviceClass.None;
    }

    private LastUsedDeviceChangedEvent m_LastUsedDeviceChangedEvent;

    public LastUsedDeviceChangedEvent lastUsedDeviceChangedEvent
    {
        get
        {
            if (m_LastUsedDeviceChangedEvent == null)
                m_LastUsedDeviceChangedEvent = new LastUsedDeviceChangedEvent();
            return m_LastUsedDeviceChangedEvent;
        }
    }

    public class LastUsedDeviceChangedEvent : UnityEvent<InputDevice> { }

#if UNITY_SWITCH && !UNITY_EDITOR
    public void ResetGyro()
    {
        if(m_connectedGamepad is NPad npad)
        {
            StopSixAxisSensors();
            StartSixAxisSensors();
            GameDebug.LogError("Resetting Gyro!");
            OnGyroReset?.Invoke();
        }
    }
#endif

    // INPUT WRAPPER
    public enum InputType
    {
        Game,
        Menu,
        GameDebug,
        Debug
    }

    public void LoadPrefs()
    {
        Controls.LoadBindingOverridesFromJson(GameDataManager.prefs.controlOverrideJson);

#if UNITY_SWITCH && !UNITY_EDITOR
        GyroEnabled = GameDataManager.prefs.gyroEnabled;
        GyroJoyCon = (GyroJoyConSide)GameDataManager.prefs.gyroJoyCon;
#endif
        ApplySensitivityByInputDeviceClass(GetLastActiveDeviceClass() == InputDeviceClass.Gamepad ? InputDeviceClass.Gamepad : InputDeviceClass.Mouse);
    }

    public bool IsUsingGamepad()
    {
        return GetLastActiveDeviceClass() == InputDeviceClass.Gamepad;
    }

    private bool CanUseInputType(InputType type)
    {
        var result = false;
        var mainMenu = MainMenu.Instance();
        
        switch (type)
        {
            case InputType.Game:
                result = !GameConsole.GetIsConsoleOpen()
                         && mainMenu != null
                         && !mainMenu.GetIsPaused()
                         && mainMenu.GetCurrentState() == MainMenu.State.None;
                break;
            case InputType.Menu:
                result = !GameConsole.GetIsConsoleOpen();
                break;
            case InputType.GameDebug:
                result = !GameConsole.GetIsConsoleOpen()
                         && mainMenu != null 
                         && !mainMenu.GetIsPaused();
                break;
            case InputType.Debug:
                result = true;
                break;
        }

        return result;
    }

    public float GetAxis(GameActions axis, InputType inputType = InputType.Game)
    {
        if (!CanUseInputType(inputType)) return 0f;

        InputAction inputAction = GetInputActionForGameAction(axis);

        if (inputAction == null) return 0f;

        switch (axis)
        {
            case GameActions.LookHorizontal:
                return inputAction.ReadValue<Vector2>().x * m_SensitivityX;

            case GameActions.LookVertical:
                return inputAction.ReadValue<Vector2>().y * m_SensitivityY;

            case GameActions.MoveHorizontal:
                return inputAction.ReadValue<Vector2>().x;

            case GameActions.MoveVertical:
                return inputAction.ReadValue<Vector2>().y;

            case GameActions.SwapCard:
                float value = inputAction.ReadValue<float>();
                return value > 0 ? 1f : value < 0 ? -1 : 0f;
        }

        return inputAction.ReadValue<float>();
    }

    public float GetAxisRaw(GameActions axis, InputType inputType = InputType.Game)
    {
        if (!CanUseInputType(inputType)) return 0f;
        
        InputAction inputAction = GetInputActionForGameAction(axis);

        if (inputAction == null) return 0f;

        switch (axis)
        {
            case GameActions.LookHorizontal:
                return inputAction.ReadValue<Vector2>().x * m_SensitivityX;

            case GameActions.LookVertical:
                return inputAction.ReadValue<Vector2>().y * m_SensitivityY;

            case GameActions.MoveHorizontal:
                return inputAction.ReadValue<Vector2>().x;

            case GameActions.MoveVertical:
                return inputAction.ReadValue<Vector2>().y;
        }

        return inputAction.ReadValue<float>();
    }

    public Quaternion GetOrientation(GameActions orientationAxis, InputType inputType = InputType.Game)
    {
        if (!CanUseInputType(inputType)) return Quaternion.identity;

#if UNITY_SWITCH && !UNITY_EDITOR
        if (m_connectedGamepad is NPad npad)
        {
            int maxHandles = 10;
            SixAxisSensorHandle[] handles = new SixAxisSensorHandle[maxHandles];
            int result =
                nn.hid.SixAxisSensor.GetHandles(handles, maxHandles, (NpadId) npad.npadId, (NpadStyle) npad.styleMask);

            Float4 outQuat = new Float4(0f, 0f, 0f, 1f);

            for (int i = 0; i < result; ++i)
            {
                SixAxisSensorState state = new SixAxisSensorState();
                nn.hid.SixAxisSensor.GetState(ref state, handles[i]);

                if (i == (int)_gyroJoyCon)
                {
                    state.GetQuaternion(ref outQuat);
                    break;
                }   
            }
            
            return new Quaternion(outQuat.x, outQuat.y, outQuat.z, outQuat.w);
        }
#endif

        InputAction inputAction = GetInputActionForGameAction(orientationAxis);

        if(inputAction == null) return Quaternion.identity;

        Quaternion unityAttitude = inputAction.ReadValue<Quaternion>();
        return unityAttitude;
    }

    private void StartSixAxisSensors()
    {
#if UNITY_SWITCH && !UNITY_EDITOR
        if (m_connectedGamepad is NPad npad)
        {
            int maxHandles = 10;
            SixAxisSensorHandle[] handles = new SixAxisSensorHandle[maxHandles];
            _availableGyroSources =
                nn.hid.SixAxisSensor.GetHandles(handles, maxHandles, (NpadId) npad.npadId, (NpadStyle) npad.styleMask);
            
            int joyConIndex = ((int)_gyroJoyCon % _availableGyroSources);
            
            for (int i = 0; i < _availableGyroSources; ++i)
            {
                if (i != joyConIndex)
                    continue;

                SixAxisSensorHandle handle = handles[i];
                nn.hid.SixAxisSensor.Start(handle);
                nn.hid.SixAxisSensor.EnableFusion(handle, false);
            }
        }
#endif
    }

    private void StopSixAxisSensors()
    {
#if UNITY_SWITCH && !UNITY_EDITOR
        if (m_connectedGamepad is NPad npad)
        {
            int maxHandles = 10;
            SixAxisSensorHandle[] handles = new SixAxisSensorHandle[maxHandles];
            _availableGyroSources =
                nn.hid.SixAxisSensor.GetHandles(handles, maxHandles, (NpadId) npad.npadId, (NpadStyle) npad.styleMask);
            
            int joyConIndex = ((int)_gyroJoyCon % _availableGyroSources);

            for (int i = 0; i < _availableGyroSources; ++i)
            {
                if(i == joyConIndex)
                    nn.hid.SixAxisSensor.Stop(handles[i]);
            }
        }
#endif
    }

    public bool GetButton(GameActions button, InputType inputType = InputType.Game)
    {
        if (!CanUseInputType(inputType)) return false;

        InputAction action = GetInputActionForGameAction(button);
        if (action != null)
            return Math.Abs(action.ReadValue<float>()) > 0f;

        return false;
    }

    public bool GetButtonUp(GameActions button, InputType inputType)
    {
        if (!CanUseInputType(inputType)) return false;
        
        InputAction action = GetInputActionForGameAction(button);
        if (action != null)
            return action.WasReleasedThisFrame();

        return false;
    }

    public bool GetButtonDown(GameActions button, InputType inputType)
    {
        if (!CanUseInputType(inputType)) return false;

        InputAction action = GetInputActionForGameAction(button);
        if (action != null)
            return action.WasPressedThisFrame();

        return false;
    }

    public string GetDeviceNameByBindingIndex(InputAction action, int bindingIndex, bool stripBraces = true)
    {
        var inputDeviceName = default(string);

        if (bindingIndex >= 0 && action.bindings.Count > bindingIndex)
        {
            string groups = action.bindings[bindingIndex].groups;
            if (!string.IsNullOrEmpty(groups))
                return groups; // So the only time we use the groups string is to add one specific device type to the action

            var bindingName = action.bindings[bindingIndex].name;
            if (!string.IsNullOrEmpty(bindingName) && bindingName.Contains("["))
            {
                inputDeviceName = bindingName.Substring(bindingName.IndexOf("[") + (stripBraces?1:0), 
                    bindingName.LastIndexOf("]") - bindingName.IndexOf("[") + (stripBraces?-1:1));
            }
            else
            {
                var bindingPath = action.bindings[bindingIndex].path;
                if (!bindingPath.Contains("<")) return null;

                inputDeviceName = bindingPath.Substring(bindingPath.IndexOf("<") + (stripBraces ? 1 : 0),
                    bindingPath.LastIndexOf(">") - bindingPath.IndexOf("<") + (stripBraces ? -1 : 1));
            }
        }

        return inputDeviceName;
    }

    public string GetKey(GameActions gameAction, int fontsize, int bindingIndex = -1, int iconScale = 120, float iconOffset = -0.1f, bool tint = false)
    {
        InputAction inputAction = GetInputActionForGameAction(gameAction);
        return GetKey(inputAction, fontsize, bindingIndex, iconScale, iconOffset, tint);
    }

    public string GetKey(InputAction inputAction, int fontsize, int bindingIndex = -1, int iconScale = 120, float iconOffset = -0.1f, bool tint = false )
    {
        if (inputAction == null) return null;
        
        string size = "";
        string voffset = "";
        string iconAtlasName = "";
        string bindingPath = "";
        string inputDevice = "";
        string bindingName = "";
        
        var deviceClass = InputDeviceClass.None;

        if (bindingIndex != -1)
        {
            bindingPath = inputAction.bindings[bindingIndex].effectivePath;
            if (!bindingPath.Contains("<")) return string.Empty;
            
            inputDevice = bindingPath.Substring(bindingPath.IndexOf("<") + 1,
                bindingPath.LastIndexOf(">") - bindingPath.IndexOf("<") - 1);
            bindingName = bindingPath.Substring(bindingPath.IndexOf("/") + 1);
            
            if (bindingPath.Contains("#"))
                bindingName = bindingPath.Substring(bindingPath.IndexOf("(") + 1,
                    bindingPath.LastIndexOf(")") - bindingPath.IndexOf("(") - 1);

            deviceClass = GetDeviceClassFromPath(inputDevice);
            if (deviceClass == InputDeviceClass.Gamepad)
            {
                size = "150";
                voffset = "-.1";
                iconAtlasName = GetControllerIconAssetName(LastUsedDevice?.name);
                return BuildKeyIconString(size, voffset, iconAtlasName, bindingName, tint);
            }
            else
            {
                size = "150";
                voffset = string.Compare(bindingName, "space", StringComparison.InvariantCultureIgnoreCase) == 0
                    ? "-.4"
                    : "-.1";
                    
#if !UNITY_SWITCH || UNITY_EDITOR
                iconAtlasName = inputDevice;
#else
                iconAtlasName = GetControllerIconAssetName(inputDevice);
#endif

                return BuildKeyIconString(size, voffset, iconAtlasName, bindingName, tint);
            }
        }

        bool bindingIsUsage = false;
        InputDeviceClass lastActiveDeviceClass = GetLastActiveDeviceClass();

        StringBuilder result = new StringBuilder();
        for (var i = 0; i < inputAction.bindings.Count; i++)
        {
            bindingPath = inputAction.bindings[i].effectivePath;
            if (bindingPath.Contains("<"))
            {
                inputDevice = bindingPath.Substring(bindingPath.IndexOf("<") + 1,
                    bindingPath.LastIndexOf(">") - bindingPath.IndexOf("<") - 1);
                bindingName = bindingPath.Substring(bindingPath.IndexOf("/") + 1);

                if (bindingPath.Contains("#"))
                    bindingName = bindingPath.Substring(bindingPath.IndexOf("(") + 1,
                        bindingPath.LastIndexOf(")") - bindingPath.IndexOf("(") - 1);

                deviceClass = GetDeviceClassFromPath(inputDevice);
            }
            else if (bindingPath.Contains("{"))
            {
                bindingIsUsage = true;
                break;
            }
            else
            {
                continue;
            }

            if (deviceClass == InputDeviceClass.Gamepad)
            {
                if (lastActiveDeviceClass != InputDeviceClass.Gamepad) continue;

                size = iconScale.ToString();
                voffset = iconOffset.ToString(CultureInfo.InvariantCulture);
                iconAtlasName = GetControllerIconAssetName(LastUsedDevice?.name);
                result.Append(BuildKeyIconString(size, voffset, iconAtlasName, bindingName, tint));

                if (!inputAction.bindings[i].isPartOfComposite) break;
            }
            else
            {
                if (lastActiveDeviceClass == InputDeviceClass.Gamepad) continue;

                size = iconScale.ToString();
                voffset = string.Compare(bindingName, "space", StringComparison.InvariantCultureIgnoreCase) == 0
                    ? "-.4"
                    : "-.1";

#if !UNITY_SWITCH || UNITY_EDITOR
                iconAtlasName = inputDevice;
#else
                iconAtlasName = GetControllerIconAssetName(inputDevice);
#endif
                result.Append(BuildKeyIconString(size, voffset, iconAtlasName, bindingName, tint));

                if (!inputAction.bindings[i].isPartOfComposite) break;
            }
        }

        if (bindingIsUsage)
        {
            for (int i = 0; i < inputAction.controls.Count; ++i)
            {
                InputControl control = inputAction.controls[i];

                deviceClass = GetDeviceClassFromControl(control);
                bindingPath = control.path;

                bindingName = bindingPath.Substring(bindingPath.LastIndexOf("/") + 1);

                if (bindingPath.Contains("#"))
                    bindingName = bindingPath.Substring(bindingPath.IndexOf("(") + 1,
                        bindingPath.LastIndexOf(")") - bindingPath.IndexOf("(") - 1);

                if (deviceClass == InputDeviceClass.Gamepad)
                {
                    if (lastActiveDeviceClass != InputDeviceClass.Gamepad) continue;

                    size = iconScale.ToString();
                    voffset = iconOffset.ToString(CultureInfo.InvariantCulture);
                    iconAtlasName = GetControllerIconAssetName(LastUsedDevice?.name);
                    result.Append(BuildKeyIconString(size, voffset, iconAtlasName, bindingName, tint));
                }
                else
                {
                    if (lastActiveDeviceClass == InputDeviceClass.Gamepad || lastActiveDeviceClass == InputDeviceClass.None) continue;

                    size = iconScale.ToString();
                    voffset = string.Compare(bindingName, "space", StringComparison.InvariantCultureIgnoreCase) == 0
                        ? "-.4"
                        : "-.1";
#if !UNITY_SWITCH
                    iconAtlasName = deviceClass.ToString();
#else
                    iconAtlasName = GetControllerIconAssetName(inputDevice);
#endif
                    result.Append(BuildKeyIconString(size, voffset, iconAtlasName, bindingName, tint));
                }
                
                break; //Break after one result as none of the available usages in InputSystem use compound inputs
                       //TODO: If InputSystem adds the ability to tell if controls are composite inputs, handle like bindings above
            }
        }


        return result.ToString();
    }

    private InputDeviceClass GetDeviceClassFromControl(InputControl control)
    {
        if (control != null)
        {
            if (control.device is Gamepad)
                return InputDeviceClass.Gamepad;
            if (control.device is Keyboard)
                return InputDeviceClass.Keyboard;
            if (control.device is Mouse)
                return InputDeviceClass.Mouse;
        }
        
        return InputDeviceClass.None;
    }

    private string BuildKeyIconString(string size, string voffset, string iconAtlasName, string bindingName, bool tint)
    {
        StringBuilder strBuilder = new StringBuilder();
        strBuilder.Append($"<size={size}%>");
        strBuilder.Append($"<voffset={voffset}em>");

        strBuilder.Append($"<sprite=\"{iconAtlasName}-Filled\"");
        strBuilder.Append($" name=\"{bindingName}\"");
        if (tint) strBuilder.Append(" tint=1");
        strBuilder.Append($">");

        strBuilder.Append("</size>");
        strBuilder.Append("</voffset>");

        return strBuilder.ToString();
    }

    private string GetControllerIconAssetName(string controllerName)
    {
        if (string.IsNullOrEmpty(controllerName)) return null;

        if (CultureInfo.InvariantCulture.CompareInfo.IndexOf(controllerName, "Controller", CompareOptions.IgnoreCase) >= 0) return "Xbox";
        if (CultureInfo.InvariantCulture.CompareInfo.IndexOf(controllerName, "Xbox", CompareOptions.IgnoreCase) >= 0) return "Xbox";
        if (CultureInfo.InvariantCulture.CompareInfo.IndexOf(controllerName, "NPad", CompareOptions.IgnoreCase) >= 0) return "Joy-Con";
        if (CultureInfo.InvariantCulture.CompareInfo.IndexOf(controllerName, "Shock", CompareOptions.IgnoreCase) >= 0) return "DualShock4";
        if (CultureInfo.InvariantCulture.CompareInfo.IndexOf(controllerName, "Sense", CompareOptions.IgnoreCase) >= 0) return "DualSense";
        
        return "Xbox";
    }

    static public bool IsSwitchController( Guid guidToTest )
    {
        return (guidToTest == new Guid("3eb01142-da0e-4a86-8ae8-a15c2b1f2a04") || guidToTest ==
            new Guid("605dc720-1b38-473d-a459-67d5857aa6ea") || guidToTest ==
            new Guid("521b808c-0248-4526-bc10-f1d16ee76bf1") || guidToTest ==
            new Guid("1fbdd13b-0795-4173-8a95-a2a75de9d204") || guidToTest ==
            new Guid("7bf3154b-9db8-4d52-950f-cd0eed8a5819"));
    }
    static public bool IsPlaystationController(Guid guidToTest)
    {
        return (guidToTest == new Guid("c3ad3cad-c7cf-4ca8-8c2e-e3df8d9960bb") || guidToTest ==
            new Guid("71dfe6c8-9e81-428f-a58e-c7e664b7fbed") || guidToTest ==
            new Guid("cd9718bf-a87a-44bc-8716-60a0def28a9f") || guidToTest ==
            new Guid("5286706d-19b4-4a45-b635-207ce78d8394"));
    }
    static public bool IsXboxController(Guid guidToTest)
    {
        return (guidToTest == new Guid("d74a350e-fe8b-4e9e-bbcd-efff16d34115") || guidToTest ==
            new Guid("19002688-7406-4f4a-8340-8d25335406c8"));
    }

    private static bool _hasInitialisedCompositeTypes = false;

    static public string GetCompositeNameForPreferredBindingType(PreferredBindingType bindingType)
    {
        if(!_hasInitialisedCompositeTypes)
            InitialiseCompositeTypes();

        switch (bindingType)
        {
            case PreferredBindingType.Axis1D:
                return nameof(AxisComposite);

            case PreferredBindingType.Axis2D:
                return nameof(Vector2Composite);

            default:
                GameDebug.LogError($"No composite exists for a preferred binding type of \"{bindingType}\"!");
                break;
        }

        return null;
    }

    private static void InitialiseCompositeTypes()
    {
        InputSystem.RegisterBindingComposite<AxisComposite>(nameof(AxisComposite));
        InputSystem.RegisterBindingComposite<Vector2Composite>(nameof(Vector2Composite));

        _hasInitialisedCompositeTypes = true;
    }

    static public string[] GetCompositeSubBindingsForPreferredBindingType(PreferredBindingType bindingType)
    {
        switch (bindingType)
        {
            case PreferredBindingType.Axis1D:
                return new string[] {"positive", "negative"};

            case PreferredBindingType.Axis2D:
                return new string[] {"up", "down", "left", "right"};

            default:
                GameDebug.LogError($"No composite exists for a preferred binding type of \"{bindingType}\"!");
                break;
        }

        return null;
    }

    private const string BindingGroupMouse = "Mouse";
    private const string BindingGroupKeyboard = "Keyboard";
    private const string BindingGroupGamepad = "Gamepad";

    static public string GetBindingGroupForDeviceClass(InputDeviceClass deviceClass)
    {
        switch (deviceClass)
        {
            case InputDeviceClass.Mouse:
                return BindingGroupMouse;

            case InputDeviceClass.Keyboard:
                return BindingGroupKeyboard;

            case InputDeviceClass.Gamepad:
                return BindingGroupGamepad;

            default:
                GameDebug.LogError($"No binding group exists for a device class of \"{deviceClass}\"!");
                break;
        }

        return null;
    }

    static public string[] GetExcludedBindingGroupsForDeviceClass(InputDeviceClass deviceClass)
    {
        switch (deviceClass)
        {
            case InputDeviceClass.Mouse:
                return new[] { BindingGroupKeyboard, BindingGroupGamepad };

            case InputDeviceClass.Keyboard:
                return new[] { BindingGroupMouse, BindingGroupGamepad };

            case InputDeviceClass.Gamepad:
                return new [] { BindingGroupMouse, BindingGroupKeyboard };

            default:
                GameDebug.LogError($"No excluded binding groups exist for a device class of \"{deviceClass}\"!");
                break;
        }

        return null;
    }
    
    private InputAction GetInputActionForGameAction(GameActions gameAction)
    {
        switch(gameAction)
        {
            case GameActions.Jump:
                return _controls.Gameplay.Jump;

            case GameActions.FireCard:
                return _controls.Gameplay.FireCard;

            case GameActions.FireCardAlt:
                return _controls.Gameplay.FireCardAlt;

            case GameActions.MoveHorizontal:
                return _controls.Gameplay.Move;

            case GameActions.MoveVertical:
                return _controls.Gameplay.Move;

            case GameActions.LookHorizontal:
                return _controls.Gameplay.Look;

            case GameActions.LookVertical:
                return _controls.Gameplay.Look;

            case GameActions.SwapCard:
                return _controls.Gameplay.SwapCard;

#if UNITY_SWITCH && !UNITY_EDITOR
            case GameActions.GyroOrientation:
                return _controls.Gameplay.GyroOrientation;

            case GameActions.GyroReset:
                return _controls.Gameplay.GyroReset;

            case GameActions.GyroSwitchSource:
                return _controls.Gameplay.GyroSwitchSource;
#endif
            
            case GameActions.Start:
                return _controls.UI.Start;

            case GameActions.Restart:
                return _controls.UI.Restart;

            case GameActions.Pause:
                return _controls.UI.Pause;

            case GameActions.Leaderboards:
                return _controls.UI.Leaderboards;

            case GameActions.UISubmit:
                return _controls.UI.Submit;

            case GameActions.UICancel:
                return _controls.UI.Cancel;

            case GameActions.DialogueFastForward:
                return _controls.UI.DialogueFastForward;

            case GameActions.MenuTabLeft:
                return _controls.UI.MenuTabLeft;

            case GameActions.MenuTabRight:
                return _controls.UI.MenuTabRight;
            
            case GameActions.DialogueAdvance:
                return _controls.UI.DialogueAdvance;
            
        }

        return null;
    }
}


