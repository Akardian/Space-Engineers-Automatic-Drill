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
        private const string PISTON_DOWN = "[Down]";
        private const string PISTON_UP = "[Up]";
        private const string DRILL_HEAD = "[Head]";
        private const float PISTON_SPEED = 01.5f;
        private const float PISTON_MAX_EXTENSION = 10.0f;
        private const float PISTON_MIN_EXTENSION = 0.0f;
        private const float ROTOR_SPEED = 0.5f;
        private const float ROTOR_MAX_ANGLE = 6.282f;
        private const float ROTOR_ANGLE_DIFFERENCE = 0.05f;
        private const string ARGUMENT_ON = "on";
        private const string ARGUMENT_OFF = "off";
        private const string ARGUMENT_RESET = "reset";
        private const string ARGUMENT_FORCE_RESET = "force_reset";
        private const string ARGUMENT_CHECK = "check"; 
        private const UpdateType COMMAND_UPDATE = UpdateType.Trigger | UpdateType.Terminal;

        private List<IMyPistonBase> _Pistons = new List<IMyPistonBase>();
        private List<IMyPistonBase> _ExtendedPistons = new List<IMyPistonBase>();
        private List<IMyShipDrill> _Drills = new List<IMyShipDrill>();

        private IMyTextPanel _LogOutput;
        private string _LcdOutput;

        private IMyMotorAdvancedStator _Rotor;
        private float _RotorMoved = 0.0f;
        float _RotorLastAngle = 0.0f;

        private enum Status : byte {Running, Stopped, Retracting, Extended, Completed, Error};
        private Status _Status = Status.Stopped;


        public Program()
        {
            try
            {
                // Replace the Echo
                Echo = EchoToLCD;
                // Fetch a log text panel
                _LogOutput = GridTerminalSystem.GetBlockWithName("Log LCD") as IMyTextPanel;
                _LogOutput.BackgroundColor = new Color(0, 0, 0, 255);
                _LogOutput.FontColor = new Color(0, 255, 0, 255);
                _LogOutput.FontSize = 0.8f;

                _LogOutput?.WriteText("");
                Echo("Drill Head LCD\n");


                List<IMyShipDrill> drillsTemp = new List<IMyShipDrill>();
                GridTerminalSystem.GetBlocksOfType(drillsTemp, drill => drill.IsSameConstructAs(Me));
                foreach (IMyShipDrill drill in drillsTemp)
                {
                    if (drill.CustomName.Contains(DRILL_HEAD))
                    {
                        _Drills.Add(drill);
                        //Echo($"Drill: {drill.CustomName}");
                    }
                }


                List<IMyMotorAdvancedStator> rotors = new List<IMyMotorAdvancedStator>();
                GridTerminalSystem.GetBlocksOfType(rotors, rotor => rotor.IsSameConstructAs(Me));
                foreach (IMyMotorAdvancedStator newRotor in rotors)
                {
                    if (newRotor.CustomName.Contains(DRILL_HEAD))
                    {
                        _Rotor = newRotor;
                    }
                }
                _RotorLastAngle = _Rotor.Angle;
                //Echo($"\nRotor: {_Rotor.CustomName}\n");

                List<IMyPistonBase> pistonTemp = new List<IMyPistonBase>();
                GridTerminalSystem.GetBlocksOfType(pistonTemp, pistonBase => pistonBase.IsSameConstructAs(Me));
                foreach (IMyPistonBase piston in pistonTemp)
                {
                    if (piston.CustomName.Contains(PISTON_UP))
                    {
                        if (piston.CurrentPosition <= MaxLimit(piston))
                        {
                            _ExtendedPistons.Add(piston);
                        } else
                        {
                            _Pistons.Add(piston);
                        }
                    }
                    if (piston.CustomName.Contains(PISTON_DOWN))
                    {
                        if (piston.CurrentPosition >= PISTON_MAX_EXTENSION)
                        {
                            _ExtendedPistons.Add(piston);
                        } else
                        {
                            _Pistons.Add(piston);
                        }
                    }
                    //Echo($"Piston: {piston.CustomName}, Position {piston.CurrentPosition}");
                }

                _Status = Status.Stopped;
                UpdateLCD();
                Runtime.UpdateFrequency = UpdateFrequency.Update100;
            }
            catch (Exception e)
            {
                Echo($"Exception: {e}\n---");
                throw;
            }
        }

        public void Save()
        {
        }

        public void Main(string argument, UpdateType updateType)
        {
            try
            { 
                if ((updateType & COMMAND_UPDATE) != 0)
                {
                    RunCommand(argument);
                }
                if ((updateType & UpdateType.Update100) != 0)
                {
                    RunContinuousLogic();
                }
            }
            catch (Exception e)
            {
                Echo($"Exception: {e}\n---");
                throw;
            }
        }

        void RunCommand(string argument)
        {
            switch (argument)
            {
                case ARGUMENT_ON:
                    ToggleDrillHead(Status.Running);
                    break;
                case ARGUMENT_OFF:
                    ToggleDrillHead(Status.Stopped);
                    break;
                case ARGUMENT_RESET:
                    RetractPistons();
                    break;
                case ARGUMENT_FORCE_RESET:
                    ForceRetractPistons();
                    break;
            }
            UpdateLCD();
        }

        void RunContinuousLogic()
        {
            try {
                if (_Pistons.Count <= 0 && _Status != Status.Extended && _Status != Status.Completed)
                {
                    _Status = Status.Extended;
                } else if (_Status == Status.Running && Math.Abs(_RotorLastAngle - _Rotor.Angle) > ROTOR_ANGLE_DIFFERENCE)
                {
                    if (_RotorLastAngle >= _Rotor.Angle)
                    {
                        _RotorMoved = _RotorMoved + (ROTOR_MAX_ANGLE - _RotorLastAngle);
                        _RotorMoved = _RotorMoved + _Rotor.Angle;
                    }
                    else
                    {
                        _RotorMoved = _RotorMoved + _Rotor.Angle - _RotorLastAngle;
                    }

                    if (_RotorMoved >= ROTOR_MAX_ANGLE)
                    {
                         _RotorMoved = 0.0f;

                         IMyPistonBase piston = null;
                         int pistonType = -1;
                        while (_Pistons.Count != 0 && piston == null)
                         {
                            piston = _Pistons.First<IMyPistonBase>();
                            if (piston.CustomName.Contains(PISTON_DOWN))
                             {
                                 if (piston.CurrentPosition >= PISTON_MAX_EXTENSION)
                                 {
                                    _Pistons.Remove(piston);
                                     _ExtendedPistons.Add(piston);
                                     piston = null;
                                 } else
                                 {
                                    pistonType = 1;
                                 }
                             } else
                             {
                                 if (piston.CurrentPosition <= PISTON_MIN_EXTENSION)
                                 {
                                     _Pistons.Remove(piston);
                                     _ExtendedPistons.Add(piston);
                                     piston = null;
                                 } else
                                 {
                                    pistonType = 2;
                                 }
                             }
                         }

                        if (pistonType == 1)
                         {
                            piston.Velocity = PISTON_SPEED;

                            float newMax = piston.CurrentPosition;
                            newMax = newMax + 2.0f;

                            if (newMax >= 10.0f)
                            {
                                piston.MaxLimit = 10.0f;    
                            } else
                            {
                                piston.MaxLimit = newMax;
                            }
                         } else if(pistonType == 2)
                         {
                            piston.Velocity = 0 - PISTON_SPEED;

                            float newMin = piston.CurrentPosition;
                            newMin = newMin - 2.0f;

                            if (newMin <= 0.0f)
                            {
                                piston.MinLimit = 0.0f;
                            }
                            else
                            {
                                piston.MinLimit = newMin;
                            }
                         }
                    
                    }
                } else  if (_Status == Status.Retracting)
                {
                    _Status = CheckRetractionStatus();
                }
                UpdateLCD();
                _RotorLastAngle = _Rotor.Angle;
            }
            catch (Exception e)
            {
                Echo($"Exception: {e}\n---");
                throw;
            }
        }

        float MaxLimit(IMyPistonBase piston)
        {
            if (piston.CustomName.Contains(PISTON_DOWN))
            {
                return 0.0f;
            } else
            {
                return 10.0f;
            }
        }

        Status CheckRetractionStatus()
        {
            foreach (IMyPistonBase piston in _Pistons)
            {
                if (piston.CustomName.Contains(PISTON_DOWN))
                {
                    if (piston.CurrentPosition != 0.0f)
                    {
                        return Status.Retracting;
                    }
                } else
                {
                    if (piston.CurrentPosition != 10.0f)
                    {
                        return Status.Retracting;
                    }
                }
            }
            return Status.Completed;
        }

        void ToggleDrillHead(Status status)
        {
            if (status == Status.Running && _Status != Status.Retracting)
            {
                //TODO: set lmits
                _Rotor.RotorLock = false;
                _Rotor.TargetVelocityRPM = ROTOR_SPEED;
                ToggleDrill(true);
                _Status = Status.Running;
            } else if(status == Status.Stopped && _Status != Status.Retracting)
            {
                _Rotor.RotorLock = true;
                ToggleDrill(false);
                _Status = Status.Stopped;
            }
        }

        void ToggleDrill(bool status)
        {
            _Rotor.Enabled = status;
            foreach (IMyShipDrill drill in _Drills)
            {
                drill.Enabled = status;
            }
        }

        void ForceRetractPistons()
        {
            ToggleDrillHead(Status.Stopped);
            _Status = Status.Retracting;

            _Pistons.AddList<IMyPistonBase>(_ExtendedPistons);
            _ExtendedPistons.Clear();

            foreach (IMyPistonBase piston in _Pistons)
            {
                if (piston.CustomName.Contains(PISTON_DOWN))
                {
                    piston.MaxLimit = 0.0f;
                    piston.MinLimit = 0.0f;
                    piston.Velocity = 0 - PISTON_SPEED;
                }
                else
                {
                    piston.MinLimit = 10.0f;
                    piston.MaxLimit = 10.0f;
                    piston.Velocity = PISTON_SPEED;
                }
            }
        }

        void RetractPistons()
        {
            if (_Pistons.Count <= 0)
            {
                ForceRetractPistons();
            }
        }

        public void UpdateLCD()
        {
            _LogOutput?.WriteText($"{_LcdOutput}", false);

            _LogOutput?.WriteText($"Not extended Pistons: {_Pistons.Count}\n", true);
            _LogOutput?.WriteText($"Extended Pistons: {_ExtendedPistons.Count}\n", true);
            _LogOutput?.WriteText($"Rotor Movment: {_RotorMoved}\n", true);
            _LogOutput?.WriteText($"Rotor last angle: {_RotorLastAngle}\n", true);
            _LogOutput?.WriteText($"Rotor current angle: {_Rotor.Angle}\n", true);
            _LogOutput?.WriteText($"CurrentInstructionCount: {Runtime.CurrentInstructionCount}\n", true);
            _LogOutput?.WriteText($"\nDrill status: {_Status}", true);
        }

        public void UpdateLCD(string text)
        {
            UpdateLCD();
            _LogOutput?.WriteText(text, true);
        }

        public void EchoToLCD(string text)
        {
            text = text + "\n";
            _LcdOutput = _LcdOutput + text; 
        }
    }
}
