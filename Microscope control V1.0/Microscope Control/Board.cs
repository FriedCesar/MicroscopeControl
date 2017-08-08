using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Data;
using System.Net.NetworkInformation;
using System.Net.WebSockets;
using System.Drawing;
using System.IO.Ports;
using System.Media;
using System.Net.Sockets;
using System.Threading;
using System.Windows.Forms;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.IO;
using System.Text;
using System.Net;
using System.ComponentModel;


//********************* Move Instuction and response command set *********************
//
//      Data Protocol:
//
//      ___________________________________________________________________________
//      |      ||            ||         ||          ||      ||            ||      |
//      | 0XAA || Session ID || Command || Motor ID || DATA || Session ID || 0XAA |
//      |      ||            ||         ||          ||      ||            ||      |
//      """""""""""""""""""""""""""""""""""""""""""""""""""""""""""""""""""""""""""
//      14 bytes
//
//      
//          Header 2 bytes
//              1st byte: Start Identifier byte (0XAA = ¬)
//              2nd byte: ASCII Encoded session ID (0-127)
//
//          End 2 bytes
//              1st byte: ASCII Encoded session ID (0-127)
//              2nd byte: End Identifier byte (0XAA = ¬)
//
//          Motor ID
//              Identifier for main or auxiliar motor (@ or ~)
//
//          Command, Data and response (8 bytes)
//              Command byte: Instruction
//              Data: Depending on function
//              Response: Motor ID + session ID + Received (Extra)
//
//              P: Movement request (Managed on MoveStage function)
//                  Data: 2 bytes
//                      1st byte: Position High byte
//                      2nd byte: Position Low byte
//                  Received: MF
//              I: Request board data (Step, cycle and time; saved on board's EEPROM)
//                  Received: IF
//                  Extra: 8 bytes
//                      1st byte: Step High byte
//                      2nd byte: Step Low byte
//                      3rd byte: Position High Byte
//                      4th byte: Position Low Byte
//                      5th byte: Time High Byte
//                      6th byte: Time Low Byte
//                      7st byte: Auxiliar Step High byte
//                      8nd byte: Auxiliar Step Low byte
//              O: Set origin request
//                  Received: OF
//              S: Movement backward cycle request (Managed on MoveStage function)
//                  Data: 2 bytes
//                      1st byte: Position High byte
//                      2nd byte: Position Low byte
//                  Received: SF
//                  *Extra: 1 byte
//                      *1st byte: Cycle
//              Z: Movement foward cycle request (Managed on MoveStage function)
//                  Data: 2 bytes
//                      1st byte: Position High byte
//                      2nd byte: Position Low byte
//                  Received: SF
//                  *Extra: 1 byte
//                      *1st byte: Cycle
//              V: Save memory request (Data on TextBoxes)
//                  Data: 8 bytes
//                      1st byte: Step High byte
//                      2nd byte: Step Low byte
//                      3rd byte: Position High Byte
//                      4th byte: Position Low Byte
//                      5th byte: Time High Byte
//                      6th byte: Time Low Byte
//                      7st byte: Auxiliar Step High byte
//                      8nd byte: Auxiliar Step Low byte
//                  Received: VF
//              Q: Stage movement speed 
//                  Data: 1 byte
//                      1st byte: Speed (8N encoding [Extended ASCII])
//                  Received: QF
//              U: Complete step selected
//                  Received: UF
//              W: uStep selected
//                  Received: WF
//              F: Forward direction selected
//                  Received: FF
//              R: Reverse direction selected
//                  Received: RF
//              A: Connect Motor
//                  Received: AF
//              D: Disconnect Motor
//                  Received: DF
//              L: Move Focus Servo
//                  Data: 1 byte
//                      1st byte: Position
//                  Received: LF                            
//***********************************************************************************
//              C: Request Temperature
//                  Received: CF
//                  Extra: 2 bytes
//                      1st byte: Temperature High Byte
//                      2nd byte: Temperature Low Byte
//              K: Send Temperature Reference
//                  Data: 2 Bytes
//                      1st byte: Temperature Reference High Byte
//                      2nd byte: Temperature Reference Low Byte
//                  Received: KF
//***********************************************************************************




namespace Microscope_Control
{

    public delegate void InstructionHandler(object sender, InstructionEventArgs e);
    public class InstructionEventArgs : EventArgs
    {
        public string ConStat { get; set; }
        public string Motor { get; set; }
        public char ID { get; set; }
    }


    //**** Auxiliar Event handler
    public delegate void AuxiliarHandler(object sender, AuxiliarEventArgs e);
    public class AuxiliarEventArgs : EventArgs
    {
        public string Request { get; set; }
    }
    //***************************

    public class Board
    {
        public class StepMotor
        {
            public bool active = false;
            public bool stepCheck = false;                              // Current state verifier (for true false options, check what is the current state from which function is called)
            public char ID;
            public int Pos = 0;                                         // Position verifier
            public int PosRef = 0;                                      // Position reference
            public int Cycle = 0;
            public string StepVal;
            public string CycleVal;
            public string TimeVal;
        }


        //***********************************************************************
        Random rnd = new Random();                          // Random session iniciator
        public byte[] session;                                     // Byte session identifier
        public byte[] sessionRx;                                   // Byte session echo
        public byte[] byteRead = new byte[12];                     // Receiver byte manager
        public bool PortSel = false;                               // Retrieves information of board connection
        public bool ConSuc = false;                                // Succesful connection flag
        public bool Busy = false;                                  // Activity monitoring flag
        public bool onConnection;
        public byte[] TxByte;                                      // Data transmision byte array (Send this)
        public string TxString;                                    // Data transmision string (Send this) 
        public string RxString;                                    // Data received string
        public int conTO = 0;                                      // Timeout connection by attempts
        public int boardTO = 30;                                 // Timeout connection by time
        public int coTO = 0;


        public float Temperature;
        public int TempRef = 37;

        private bool accept;

        //private static object locker = new object();                // Locker, used to securely manage data (image stream for liveview)
        System.Timers.Timer timer = new System.Timers.Timer();

        public StepMotor MainMotor = new StepMotor();
        public StepMotor AuxMotor = new StepMotor();
        public SerialPort PortCOM = new SerialPort();

        public event InstructionHandler Instruction;
        InstructionEventArgs args = new InstructionEventArgs();
        protected virtual void OnInstruction(InstructionEventArgs e)
        {
            Instruction?.Invoke(this, e);
        }

        //************ Auxiliar event
        public event AuxiliarHandler Auxiliar;
        AuxiliarEventArgs argsAux = new AuxiliarEventArgs();
        protected virtual void OnAuxiliar(AuxiliarEventArgs e)
        {
            Auxiliar?.Invoke(this, e);
        }
        //**********************************


        public Board()
        {
            timer.Elapsed += new System.Timers.ElapsedEventHandler(timer_Elapsed);
            PortCOM.DataReceived += PortCOM_DataReceived;
            MainMotor.ID = '@';
            AuxMotor.ID = '~';
            if (PortCOM.IsOpen)
                PortCOM.Close();
        }

        private void SendData(byte[] message)
        {
            //lock (locker)
            //{
            AuxSender("RxON");
            if (PortCOM.IsOpen)
            {
                Busy = true;
                byte[] instruction;
                byte[] start = new byte[] { Convert.ToByte('¬'), session[0] };
                byte[] tail = new byte[] { session[0], Convert.ToByte('¬') };
                TxString = Encoding.ASCII.GetString(message);
                if ((TxString.Contains("@DISCONNECT")) | (TxString.Contains("@COMERROR")) | (TxString.Contains("@COMREQU")))
                {
                    instruction = message;
                }
                else
                {
                    instruction = new byte[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
                    for (int i = 0; i < message.Length; i++)
                    {
                        instruction[i] = message[i];
                    }
                }

                byte[] sendthis = start.Concat(instruction).Concat(tail).ToArray();

                TxByte = sendthis;
                string mySession = Encoding.ASCII.GetString(session);
                TxString = Encoding.Default.GetString(TxByte);
                PortCOM.Write(sendthis, 0, sendthis.Length);
            }
            AuxSender("RxOFF");
            //}
        }

        public void StartSerial()
        {
            if (PortCOM.IsOpen)
            {
                PortCOM.Dispose();
                PortCOM.Close();
            }

            session = new byte[] { Convert.ToByte(rnd.Next(1, 128)) };  // Generates a session number byte
            PortCOM.Open();                                             // Opens Port
            timer.Interval = boardTO;
            timer.Enabled = true;
            SendData(Encoding.ASCII.GetBytes("@COMREQU"));

        }

        public void StopSerial(object sender, EventArgs e)
        {
            //SendData(Encoding.ASCII.GetBytes("@DISCONNECT"));                                                        // Send Disconnection request (board's led will blink three times)
            //PortCOM.Dispose();
            if (PortCOM.IsOpen)
            {
                SendData(Encoding.ASCII.GetBytes("@DISCONNECT"));
                Thread.Sleep(10);
                PortCOM.Close();                                                                    // Close Port and reset flags
            }
            ConSuc = false;
            PortSel = false;
            RxString = "";
            Busy = false;
            MainMotor.active = false;
            AuxMotor.active = false;
            StatSender("disconnect", '¬');
        }

        private void Connect(object sender, EventArgs e)                                        // Manages on connection actions. This routine has been designed in order to avoid communnication errors (Tested on errors, the normal behavior should not have any)
        {
            if (RxString.Contains("COMSTART"))                                                      // on communication request, "COMSTART" is the identifier generated on the board. This instruction comes with an extra byte, session, which is used along the process to verify proper work.
            {
                timer.Enabled = false;
                if (coTO>0)
                {
                    StatSender("error" + coTO.ToString("D2"), '¬');
                }
                coTO = 0;
                sessionRx = new byte[] { Convert.ToByte(RxString.ElementAt(RxString.Length - 1)) }; // Extracts session byte from command
                if (BitConverter.ToString(sessionRx) == BitConverter.ToString(session))             // Compares session, if succesful, then connect
                {
                    ConSuc = true;
                    //Activate(MainMotor);
                    //MainMotor.active = true;
                    //Thread.Sleep(50);
                    //Activate(AuxMotor);
                    //AuxMotor.active = true;
                    StatSender("connected", '¬');
                    conTO = 0;
                }
                else
                {
                    if ((conTO < 100) & !ConSuc)                                                            // Manages connection timeout, if connection is not succesful, it will reinitiate connection protocol
                    {
                        PortCOM.DiscardInBuffer();
                        PortCOM.DiscardOutBuffer();
                        conTO += 1;
                        PortSel = true;
                        if (conTO == 100)                                                           // On timeout (100 attempts) display error
                        {
                            conTO = 101;
                            StatSender("failed", '¬');
                            SendData(Encoding.ASCII.GetBytes("@COMERROR"));                                                 // Sends error request
                        }
                        else
                        {
                            string mySession = Encoding.ASCII.GetString(session);
                            session = new byte[] { Convert.ToByte(rnd.Next(1, 128)) };
                            StatSender("error" + conTO.ToString("D2"), '¬');
                            SendData(Encoding.ASCII.GetBytes("@COMREQU"));
                        }

                    }

                }
            }
            else
            {
                if (RxString.Contains("DISCONNECT"))                                                    // Disconnect request received TODO: Check disconnection on error
                {
                    StopSerial(sender, e);
                }
                else
                {
                    conTO += 1;
                    string mySession = Encoding.ASCII.GetString(session);
                    session = new byte[] { Convert.ToByte(rnd.Next(1, 128)) };
                    StatSender("error" + conTO.ToString("D2"), '¬');
                    SendData(Encoding.ASCII.GetBytes("@COMREQU"));
                }
            }
        }



        private void PortCOM_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            AuxSender("RxON");
            conTO = 0;
            Array.Resize(ref byteRead, PortCOM.BytesToRead);
            PortCOM.Read(byteRead, 0, PortCOM.BytesToRead);
            RxString = Encoding.UTF8.GetString(byteRead);
            AuxSender("Instruction");
            if (RxString == "@#¬#@")                                                              // Error message, sends last instruction
            {
                StatSender("insFailed", '¬');
                PortCOM.Write(TxByte, 0, TxByte.Length);
            }
            else
            {
                if (!ConSuc)                                                                            // Activates connection routine if no connection is stablished
                {
                    Connect(sender, e);
                }
                else
                {
                    if (RxString.Length < 12)
                        Thread.Sleep(100);
                    if (PortCOM.IsOpen)
                    {
                        if (PortCOM.BytesToRead > 0)
                        {
                            byte[] byteDiff = new byte[PortCOM.BytesToRead];
                            Array.Resize(ref byteRead, PortCOM.BytesToRead + byteRead.Length);
                            PortCOM.Read(byteDiff, 0, PortCOM.BytesToRead); ;
                            byteDiff.CopyTo(byteRead, byteRead.Length - byteDiff.Length);
                            RxString = Encoding.UTF8.GetString(byteRead);
                        }
                    }
                    if (RxString.Length == 12)
                    {
                        accept = false;
                        if (RxString.Substring(0, 2) == ("@" + Encoding.ASCII.GetString(session)))
                        {
                            ComInstruction(ref MainMotor);                                           // Manages connection//deconnection error report
                            accept = true;
                        }
                        if (RxString.Substring(0, 2) == ("~" + Encoding.ASCII.GetString(session)))
                        {
                            ComInstruction(ref AuxMotor);
                            accept = true;
                        }
                        if (!accept)
                        {
                            PortCOM.Write("REPEAT");
                        }
                    }
                    else
                    {
                        if (RxString.Length != 0)
                            PortCOM.Write("REPEAT");
                    }
                }
            }

            if (PortCOM.IsOpen)
                PortCOM.DiscardInBuffer();
            AuxSender("RxOFF");
        }

        public void ReqInfo()
        {
            Busy = true;
            SendData(Encoding.ASCII.GetBytes("I@"));
        }

        public void SaveData(string Step, string Cycle, string Time, string AuxStep)
        {
            Busy = true;
            byte[] byteStep = BitConverter.GetBytes(Convert.ToInt16(Step));
            byte[] byteCycle = BitConverter.GetBytes(Convert.ToInt16(Cycle));
            byte[] byteTime = BitConverter.GetBytes(Convert.ToInt16(Time));
            byte[] byteStepAux = BitConverter.GetBytes(Convert.ToInt16(AuxStep));
            byte[] sendthis = new byte[] { Convert.ToByte('V'), Convert.ToByte('@'), byteStep[0], byteStep[1], byteCycle[0], byteCycle[1], byteTime[0], byteTime[1], byteStepAux[0], byteStepAux[1] };
            string message = Encoding.ASCII.GetString(sendthis);
            SendData(sendthis);
        }

        public void MoveStage(ref StepMotor thisMotor, int step, char type)
        {
            //lock (locker)
            {
                Busy = true;
                thisMotor.Pos = step;
                byte[] bytePos = BitConverter.GetBytes(step);
                byte[] sendthis = new byte[] { Convert.ToByte(type), Convert.ToByte(thisMotor.ID), bytePos[0], bytePos[1] };
                string message = Encoding.ASCII.GetString(sendthis);
                StatSender("Moving", thisMotor.ID);
                SendData(sendthis);
            }
        }

        public void SetOrigin(ref StepMotor thisMotor)
        {
            Busy = true;                                                                            // Sets busy flag
            //Thread.Sleep(5);
            SendData(Encoding.ASCII.GetBytes("O" + thisMotor.ID));
        }

        public void ChangeSpeed(ref StepMotor thisMotor, int speed)
        {
            Busy = true;
            byte[] byteSpeed = BitConverter.GetBytes(speed);
            byte[] sendthis = new byte[] { Convert.ToByte('Q'), Convert.ToByte(thisMotor.ID), byteSpeed[0] };
            string message = Encoding.ASCII.GetString(sendthis);
            SendData(sendthis);
        }

        public void uStep(ref StepMotor thisMotor, bool Checked)
        {
            Busy = true;                                                                            // Sets busy flag
            thisMotor.stepCheck = Checked;
            if (thisMotor.stepCheck)
            {
                SendData(Encoding.ASCII.GetBytes("W" + thisMotor.ID));
            }
            else
            {
                SendData(Encoding.ASCII.GetBytes("U" + thisMotor.ID));
            }
        }

        public void ChangeDirection(ref StepMotor thisMotor, bool Checked)
        {
            Busy = true;                                                                            // Sets busy flag
            thisMotor.stepCheck = Checked;
            if (thisMotor.stepCheck)
            {
                SendData(Encoding.ASCII.GetBytes("R" + thisMotor.ID));
            }
            else
            {
                SendData(Encoding.ASCII.GetBytes("F" + thisMotor.ID));
            }
        }

        public void Activate(StepMotor thisMotor)
        {
            Busy = true;
            SendData(Encoding.ASCII.GetBytes("A" + thisMotor.ID));
        }

        public void Deactivate(StepMotor thisMotor)
        {
            Busy = true;
            SendData(Encoding.ASCII.GetBytes("D" + thisMotor.ID));
        }

        public void MoveServo(int position)
        {
            Busy = true;
            byte[] sendthis = new byte[] { Convert.ToByte('L'), Convert.ToByte('~'), Convert.ToByte(position) };
            string message = Encoding.ASCII.GetString(sendthis);
            SendData(sendthis);
        }

        public void SendTempRef(int Temperature)
        {
            Busy = true;

            byte[] byteTemp = BitConverter.GetBytes(Temperature);
            byte[] sendthis = new byte[] { Convert.ToByte('K'), Convert.ToByte('@'), byteTemp[0], byteTemp[1] };
            string message = Encoding.ASCII.GetString(sendthis);
            SendData(sendthis);
        }

        public void RequTemperature()
        {
            Busy = true;
            SendData(Encoding.ASCII.GetBytes("C@"));
        }

        private void StatSender(string status, char id)
        {
            AuxSender("RxON");
            args.ID = id;
            args.ConStat = status;
            if (id == '@')
                args.Motor = "Main";
            if (id == '~')
                args.Motor = "Auxiliar";
            OnInstruction(args);
            AuxSender("RxOFF");
        }

        private void AuxSender(string type)
        {
            argsAux.Request = type;
            OnAuxiliar(argsAux);
        }

        private void ComInstruction(ref StepMotor myMotor)                                 // Manages received instructions from board (and actions on request)
        {
            //lock (locker)
            //{
            //AuxSender("RxON");
            bool receivedAction = false;
            string lookup = "";
            string command = "";
            //if (RxString.Length >= 4)                                                               // Reads connection encoding and instruction
            //{
            lookup = RxString.Substring(0, 2);
            command = RxString.Substring(2, 2);
            //}
            switch (command)                                                                    // Reads command and checks action (or none)
            {
                case "MF":                                                                     // Move Finished (Answers to 'P' request)                                                                                                //textBox1.Text = (Arduino.MainMotor.Pos + ", " + Arduino.MainMotor.PosRef);
                    receivedAction = true;
                    //myMotor.RxPos = BitConverter.ToUInt16(byteRead, 4) - 32;
                    if (myMotor.Pos == myMotor.PosRef)                                                          // Check if position is up-to-date
                    {
                        Busy = false;
                        StatSender("MoveFinished", myMotor.ID);
                    }
                    else
                    {
                        Busy = true;
                        MoveStage(ref myMotor, myMotor.PosRef, 'P');
                        StatSender("MoveIncomplete", myMotor.ID);                                              // Sends movement request to board
                    }
                    break;
                case "IF":                                                                      // Information received
                    if (byteRead.Length == 12)
                    {
                        receivedAction = true;                                       // Decode and allocate data
                        Busy = false;
                        //Thread.Sleep(50);
                        MainMotor.StepVal = BitConverter.ToUInt16(byteRead, 4).ToString();
                        MainMotor.CycleVal = BitConverter.ToUInt16(byteRead, 6).ToString();
                        MainMotor.TimeVal = BitConverter.ToUInt16(byteRead, 8).ToString();
                        AuxMotor.StepVal = BitConverter.ToUInt16(byteRead, 10).ToString();
                        StatSender("DataInfo", myMotor.ID);
                        break;
                    }
                    else
                    {
                        Busy = true;
                        receivedAction = false;
                    }
                    break;
                case "OF":                                                                      // Origin stablished
                    receivedAction = true;
                    Busy = false;
                    myMotor.Pos = 0;
                    myMotor.PosRef = 0;
                    myMotor.Cycle = 0;
                    StatSender("Origin", myMotor.ID);
                    break;
                case "SF":                                                                      // Cycle completed (Then sends board request for stablishing origin)
                    receivedAction = true;
                    Busy = false;
                    myMotor.Pos = 0;
                    myMotor.PosRef = 0;
                    StatSender("Cycle", myMotor.ID);
                    break;
                case "VF":                                                                      // Save completed
                    receivedAction = true;
                    Busy = false;
                    StatSender("DataSaved", myMotor.ID);
                    break;
                case "LF":
                    receivedAction = true;
                    Busy = false;
                    StatSender("ServoMoved", myMotor.ID);
                    break;
                //case "AF":
                //    receivedAction = true;
                //    Busy = false;
                //    myMotor.active = true;
                //    StatSender("Activated", myMotor.ID);
                //    break;
                //case "DF":
                //    receivedAction = true;
                //    Busy = false;
                //    myMotor.active = false;
                //    StatSender("Deactivated", myMotor.ID);
                //    break;
                case "CF":
                    receivedAction = true;
                    int temp = BitConverter.ToUInt16(byteRead, 4);
                    Temperature = (float)((temp * 500.0) / 1024.0);
                    //*****************For LM19***********
                    //var tempF = (5.0*temp) / 1024;
                    //MessageBox.Show(temp.ToString());
                    //Temperature = (float)(Math.Sqrt((2.1962 * Math.Pow(10, 6)) + ((1.8639 - tempF) / (3.88 * Math.Pow(10, -6)))) - 1481.96);
                    //*************************************
                    Busy = false;
                    StatSender("temperature", '¬');
                    break;
                case "AF":
                case "DF":
                case "RF":                                                                      // Completed reverse direction selection
                case "FF":                                                                      // Completed forward direction selection
                case "QF":                                                                      // Completed speed selection
                case "UF":                                                                      // Completed normal step selection
                case "WF":                                                                      // Completed uStep selection
                case "KF":
                    receivedAction = true;
                    Busy = false;
                    break;
                default:
                    receivedAction = false;
                    break;
            }
            //AuxSender("RxOFF");
            if (!receivedAction)                                                                    // If no correct response from board is received, send again board request
            {
                PortCOM.Write(TxByte, 0, TxByte.Length);
            }
            //}
        }



        private void timer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            coTO++;
            timer.Enabled = false;
            PortCOM.DiscardInBuffer();
            PortCOM.DiscardOutBuffer();
            StartSerial();
        }

    }




}
