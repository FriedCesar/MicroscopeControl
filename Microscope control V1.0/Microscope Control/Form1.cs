////////////////*   Program for automatic movement control of a microscope (Stage) via step motor and image automatic capture (Using Sony DSC-QX10) */////////////
//
// Program for automatic movement control of a microscope (Stage) via step motor and image automatic capture (Using Sony DSC-QX10)
// 
// César Augusto Hernández Espitia
// ca.hernandez11@uniandes.edu.co
//
// V1.0      June 2017
// Program designed to be connected with an ARDUINO MEGA
// Designed to be connected with a Sony DSC-QX10 camera (It uses the Sony's Remote Camera API SDK; small changes can be implemented to extend range)
// Uses two step motors (Using A4988 MotorDriver), a servo Motor (standard 5V, managed directly on and arduino output signal)
// It has been designed altoguether with a cell to keep sample on a given temperature: The designed cell was 3D printed, it allows theincorporation of a miniature sized heater and a temperature sensor (LM35). Some ports are available to include further services. (additional Energy supply must be used, a 12V 2Amp supply is recommended. The temperature control is managed by the arduino board (ON/OFF). Heater control can be done with a relay or an optocoupler (Both options in hardware))
// The design of a board to ease connections from the Arduino board and the peripherial hardware has been implemented
// 
// 
// Notes:
//          Camera MUST be connected to PC before attempting to connect to the program (This program lacks a discovery device method for the camera)
//          Version still as a prototype, Be careful not to overload the programm with orders (Be gentle)
//          Bugs might be present, this program has not been thoughtfully tested
//          This program is designed to work altogether with an ARDUINO board, thus, the ARDUINO code for the used board is necessary
// TODO:
//          Checkfor AF algorithms
//
//
// Camera Remote API by Sony
//
//
///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Media;
using System.Net;
using System.Threading;
using System.Windows.Forms;
using Newtonsoft.Json;
using System.Text;

namespace Microscope_Control
{
    public partial class Form1 : Form
    {


        // The following Variables are related to the camera behavior
        // Type definition of Camera related variables

        Camera SonyQX10 = new Camera();                         // Camera class declaration
        int i = 0;                                              // Multipropose counter
        string RootPath;                                        // Stores and manages the main folder path
        string PicPath;                                         // Stores and manages the images folder path
        private static object locker = new object();            // Locker, used to securely manage data and process


        // The following Variables are related to the board managing and communication
        // Type definition of Stage related variables

        Board Arduino = new Board();                            // Board class declaration
        Random rnd = new Random();                              // Random session iniciator
        byte[] session;                                         // Byte session identifier
        byte[] byteRead = new byte[12];                         // Receiver byte manager


        // The following Variables are related to the automated observation
        // Type definition of Automated observation

        bool Auto = false;                                      // Automated movement active flag
        bool BoardData = false;                                 // Data change identifier flag
        bool Calibrated = false;                                // Calibration routine flag
        bool unmanaged = false;                                 // Unmanaged capture Flag
        bool onCapture = false;                                 // Active capture flag   
        bool onCalibration = false;                             // Active calibration flag        
        bool onMove = false;                                    // Movement finished flag
        bool onSave = false;                                    // Image saved flag
        bool onAuto = false;                                    // Automation identifier flag
        bool onReCal = false;                                   // Recalibration flag
        int myFrame = 0;                                        // Frame counter
        int myImg = 0;                                          // Image counter
        int frameCount = 0;                                     // Frame verifier
        int TotalFrames;                                        // Frame number store variable
        int TotalTime;                                          // Time number store variable
        int echoTmr = 0;                                            // Counter for echo timer (Repeat instruction in case of error)
        int[] MainAct;                                          // Main motor position array for not calibrated routine
        int[] AuxiliarAct;                                      // Auxiliar motor position array for not calibrated routine
        int[] Main;                                             // Main motor position array for calibrated routine
        int[] Auxiliar;                                         // Auxiliar motor position array for calibrated routine
        int[] FocusServo;                                       // Focus(Servo) motor position array for calibrated routine
        string request;                                         // Request identifier for automated routine
        string response;                                        // Request method identifier for automated routine

        int AuxTmrProg = 0;                                     // Timer information for avoiding communication overflow


        public Form1()
        {
            InitializeComponent();
            CheckForIllegalCrossThreadCalls = false;            // Allow cross thread calls
            ImgGuide.BackColor = Color.Transparent;             // Loads transparent color for image guide
            ImgGuide.Parent = ImgLiveview;                      // Sets liveviewimage as image guide parent
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            //************************* Create event handlers ********************************************
            SonyQX10.ConnectEvent += SonyQX10_ConnectEvent;                                                 // Manages camera events
            Arduino.Instruction += Arduino_Instruction;                                                     // Manages Board main events
            Arduino.Auxiliar += Arduino_Auxiliar;                                                           // Manages Board auxiliar events
            //************************* Check for root folder path for file management *******************
            i = 1;
            RootPath = ("C:\\Observation\\" + DateTime.Now.ToString("yyMMdd"));                             // Creates root storage file (C://observation//(Date))
            while (Directory.Exists(RootPath))                                                              // Check if requested directory exists, if so, an extra number is added
            {
                RootPath = ("C:\\Observation\\" + DateTime.Now.ToString("yyMMdd") + ("_") + i.ToString("D2"));
                i += 1;
            }
            //************************* Visualization ****************************************************
            checkBox2.Checked = true;
            View_start();
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)                   // On close, zoom camera out, disconnect board
        {
            if (SonyQX10.CamConStatus)
            {
                SonyQX10.CamResponse = SonyQX10.SendRequest("actZoom", "\"out\",\"start\"");
                ImgLiveview.Visible = false;
                SonyQX10.FlagLvw = false;
                SonyQX10.LiveView.CancelAsync();
                SonyQX10.CamResponse = SonyQX10.SendRequest("stopLiveview", "");
                guideChkBtn.Enabled = false;
            }
            if (Arduino.PortCOM.IsOpen == true)
                Arduino.StopSerial(sender, e);
        }

        private void WriteReport(string WriteThis)                                              // Writes data (string WriteThis) to report file (on root file)
        {
            if (!Directory.Exists(RootPath))                                                            // Check requested directory exists, if not, creates it
            {
                DirectoryInfo di = Directory.CreateDirectory(RootPath);
            }
            if (!File.Exists(RootPath + "\\Report.txt"))
            {
                using (StreamWriter sw = File.CreateText(RootPath + "\\Report.txt"))
                {
                    sw.WriteLineAsync("OBSERVATION REPORT\r\n" + DateTime.Now.ToString("dddd MMMM dd yyyy, hh:mm:ss tt") + "\r\n");
                }
            }
            using (StreamWriter sw = File.AppendText(RootPath + "\\Report.txt"))                        // Appends requested text to Report file
                sw.WriteLineAsync(WriteThis);

        }



        // The following code manages the Visualization routines
        //          TODO:

        bool[] mainBool = { true, false, false, false };
        bool[] auxBool = { false, true, false, false };
        bool[] focusBool = { false, false, true, false };
        bool[] tempBool = { false, false, false, true };

        private void View_start()
        {
            List<string> protectedControl = new List<string>(new string[] { "CameraPanel", "BoardConnectionPanel", "ImgLogo" });
            List<string> protectedControl2 = new List<string>(new string[] { "ConnectBtn", "BConnectionCBox", "progressBar1" });
            foreach (Control control in Controls)
            {
                if ((control is Panel) | (control is PictureBox))
                {
                    if (protectedControl.Any(control.Name.Contains))
                        control.Visible = true;
                    else
                        control.Visible = false;
                    if (control is Panel)
                    {
                        foreach (Control panelControl in control.Controls)
                        {
                            if ((control.Name == "TestControlPanel") | protectedControl2.Any(panelControl.Name.Contains))
                                panelControl.Enabled = true;
                            else
                                panelControl.Enabled = false;
                        }
                    }
                }
                else
                    control.Enabled = false;
            }
            BConnectionCBox.Items.Clear();
            BConnectionCBox.Items.Add("Select Port");
            BConnectionCBox.SelectedIndex = 0;
            checkBox2.Checked = true;
            protectedControl.Clear();
            Update();
        }

        private void View_camera(bool state)
        {
            foreach (Control control in CameraPanel.Controls)
            {
                control.Enabled = state;
                if (control is CheckBox)
                {
                    if (control.Name == "resolutionChkBtn")
                        ((CheckBox)control).Checked = state;
                    else
                        ((CheckBox)control).Checked = false;
                }
            }
            ConnectionTxt.Visible = !state;
            Update();
        }

        private void View_liveview(bool state)
        {
            ImgLiveview.Visible = state;
            ImgLiveview.Enabled = state;
            ImgLogo.Visible = !state;
            ImgLogo.Enabled = !state;
            guideChkBtn.Enabled = state;
            Update();
        }

        private void View_guide(bool state)
        {
            ImgGuide.Visible = state;
            guideRefreshBtn.Enabled = state;
            Update();
        }

        private void View_capture(bool state)
        {
            CapturePanel.Visible = state;
            StartBtn.Enabled = state;
            CaptureBtn.Enabled = false;
            ManageChkBtn.Enabled = state;
            progressBar1.Visible = false;
            calibrationChkBtn.Enabled = false;
            reCalibrationChkBtn.Enabled = false;
            //if (SonyQX10.FlagLvw)
                CalibrationBtn.Enabled = SonyQX10.FlagLvw;
            Update();
        }

        //private void View_board(bool state)
        //{
        //    View_board(state, false, false, false, false);
        //}

        private void View_board(bool state, bool[] selector)
        {
            bool main = selector[0];
            bool aux = selector[1];
            bool focus = selector[2];
            bool temp = selector[3];
            View_board(state, main, aux, focus, temp);
        }

        private void View_board(bool state, bool main = false, bool aux = false, bool focus = false, bool temp = false)
        {
            List<Panel> activePanel = new List<Panel>();
            if (!main & !aux & !focus & !temp)
            {
                main = true;
                aux = true;
                focus = true;
                temp = true;
                BoardPanel.Visible = state;
                BoardAuxPanel.Visible = state;
                FocusPanel.Visible = state;
                TempPanel.Visible = state;
                foreach (Control control in BoardConnectionPanel.Controls)
                {
                    if (control is CheckBox)
                    {
                        ((CheckBox)control).Checked = state;
                        ((CheckBox)control).Enabled = state;
                    }
                }
            }
            if (main)
            {
                activePanel.Add(BoardPanel);
                BCycle1Btn.Enabled = false;
                Arduino.MainMotor.Cycle = 0;
                BSpeedTB.Value = 3;                                                                     // Manages form layout (Disable microscope control buttons) TODO: Find a more ellegant way to do this
                BStepTB.Value = 0;
                BStepTB.Maximum = 100;
                BStepTBLbl.Text = ("Step (Main):");
                BCycleCountLbl.Text = ("0");
                BStepMaxLbl.Text = ("Max: 100");
            }
            if (aux)
            {
                activePanel.Add(BoardAuxPanel);
                BACycle1Btn.Enabled = false;
                Arduino.AuxMotor.Cycle = 0;
                BASpeedTB.Value = 3;                                                                     // Manages form layout (Disable microscope control buttons) TODO: Find a more ellegant way to do this
                BAStepTB.Value = 0;
                BAStepTB.Maximum = 100;
                BAStepTBLbl.Text = ("Step (Aux):");
                BACycleCountLbl.Text = ("0");
                BAStepMaxLbl.Text = ("Max: 100");
            }
            if (focus)
            {
                activePanel.Add(FocusPanel);
            }
            if (temp)
            {
                activePanel.Add(TempPanel);
            }
            foreach (Panel panel in activePanel)
            {
                panel.Enabled = state;
                foreach (Control control in panel.Controls)
                {
                    if (control is CheckBox)
                    {
                        ((CheckBox)control).Checked = false;
                    }
                    if (panel.Name == "FocusPanel")
                    {
                        if ((!(control is CheckBox) & (!(control is Button))))
                            control.Enabled = state;
                        if ((control is Button) | (control.Name == "calibrationChkBtn"))
                            control.Enabled = Calibrated;
                    }
                    else
                        control.Enabled = (!(control.Name.Contains("Cycle1Btn")));
                }
            }
            Update();
        }

        private void View_automated(bool state, string type)
        {
            List<string> usedControls = new List<string>();
            switch (type)
            {
                case "main":
                    usedControls = new List<string>(new string[] { "focusTB", "SpeedTB", "StepTB", "Img", "uStepChkBtn", "StepMax1Btn", "StepMax2Btn", "StepMaxLbl" });

                    progressBar1.Visible = !state;
                    CameraPanel.Enabled = state;
                    BoardConnectionPanel.Enabled = state;
                    if (TempChkBox.Checked)
                        TempPanel.Enabled = state;
                    else
                        TempPanel.Enabled = false;
                    if (FocusChkBox.Checked)
                    {
                        if (unmanaged)
                        {
                            CaptureBtn.Enabled = false;
                            FocusPanel.Enabled = state;
                        }
                        else
                        {
                            CaptureBtn.Enabled = !state;
                            FocusPanel.Enabled = true;
                        }
                    }
                    else
                        FocusPanel.Enabled = false;
                    if (Arduino.MainMotor.active)
                        BoardPanel.Enabled = state;
                    else
                        BoardPanel.Enabled = false;
                    if (Arduino.AuxMotor.active)
                        BoardAuxPanel.Enabled = state;
                    else
                        BoardAuxPanel.Enabled = false;
                    if (onCalibration)
                    {
                        usedControls.Add("CalibrationBtn");
                        progressBar1.Visible = false;
                    }
                    else
                        CalibrationBtn.Enabled = false;
                    
                    if (calibrationChkBtn.Checked & onAuto)
                        usedControls.Add("reCalibrationChkBtn");
                    else
                        reCalibrationChkBtn.Enabled = false;

                    if (Arduino.MainMotor.active)
                    {
                        foreach (Control control in BoardPanel.Controls)
                        {
                            if (usedControls.Any(control.Name.Contains))
                                control.Enabled = true;
                            else
                                control.Enabled = state;
                        }
                        if (Arduino.AuxMotor.active)
                        {
                            foreach (Control control in BoardAuxPanel.Controls)
                            {
                                if (usedControls.Any(control.Name.Contains))
                                    control.Enabled = true;
                                else
                                    control.Enabled = state;
                            }
                        }

                        if (FocusChkBox.Checked)
                        {
                            foreach (Control control in FocusPanel.Controls)
                            {
                                if (usedControls.Any(control.Name.Contains))
                                    control.Enabled = true;
                                else
                                {
                                    if (control.Name.Contains("alibration"))
                                    {
                                        if (Calibrated)
                                        {
                                            calibrationChkBtn.Enabled = state;
                                            reCalibrationChkBtn.Enabled = !state;
                                        }
                                        else
                                        {
                                            calibrationChkBtn.Enabled = false;
                                            reCalibrationChkBtn.Enabled = false;
                                        }
                                        CalibrationBtn.Enabled = SonyQX10.FlagLvw & state;
                                    }
                                    else
                                        control.Enabled = state;
                                }
                            }
                        }

                    }

                    break;
                case "calibrate":
                    BoardPanel.Enabled = true;
                    BoardAuxPanel.Enabled = true;
                    FocusPanel.Enabled = true;
                    break;
                default:
                    break;
            }
            Update();
        }



        // The following code is (Mostly) related to the managing of the Camera
        //      TODO:

        private void ConnectBtn_Click(object sender, EventArgs e)                               // Manages the discovery routine to connect with camera DSC-QX10 (Must be connected to PC WiFi)
        {
            View_camera(false);
            if (!SonyQX10.CamConStatus)                                                         // If cammera is not connected, starts connection routine
                SonyQX10.Connect.RunWorkerAsync();                                              // Send action request to camera host to stablish connection
            else                                                                                // If cammera is connected, starts disconnection routine
            {
                if (SonyQX10.FlagLvw)
                {
                    View_liveview(false);
                    CalibrationBtn.Enabled = false;
                    SonyQX10.FlagLvw = false;
                    SonyQX10.LiveView.CancelAsync();
                    SonyQX10.CamResponse = SonyQX10.SendRequest("stopLiveview", "");            // Send action request to camera host to stop liveview
                    guideChkBtn.Enabled = false;
                }
                Calibrated = false;                                // Calibration routine flag
                calibrationChkBtn.Checked = false;
                View_capture(false);
                ConnectionTxt.Text = "Disconnecting Camera";
                SonyQX10.Disconnect.RunWorkerAsync();
            }
        }

        private void LiveviewBtn_Click(object sender, EventArgs e)                              // Manages beginning/end of liveview
        {
            if (!SonyQX10.FlagLvw)
            {
                //************************ Start liveview Background Worker (Send HTTP GET request, calls Liveview Background worker)
                if (Arduino.MainMotor.active)
                    CalibrationBtn.Enabled = true;
                SonyQX10.FlagLvw = true;
                SonyQX10.CamResponse = SonyQX10.SendRequest("startLiveview", "");               // Send action request to camera host to start liveview
                SonyQX10.lvwURL = SonyQX10.ReadRequestJson(SonyQX10.CamResponse, 0);            // Setup the URL for the liveview download
                WebRequest lvwRequest = WebRequest.Create(SonyQX10.lvwURL);                     // Create a request using the camera liveview URL, send HTTP GET request
                lvwRequest.Method = "GET";
                lvwRequest.ContentType = "application/x-www-form-urlencoded; charset=UTF-8";
                SonyQX10.imgStream = lvwRequest.GetResponse().GetResponseStream();              // Setup and get the request stream response
                SonyQX10.imgReader = new StreamReader(SonyQX10.imgStream);
                if (!SonyQX10.LiveView.IsBusy)
                    SonyQX10.LiveView.RunWorkerAsync();
                View_liveview(true);
            }
            else
            {
                CalibrationBtn.Enabled = false;
                SonyQX10.FlagLvw = false;
                SonyQX10.LiveView.CancelAsync();
                SonyQX10.CamResponse = SonyQX10.SendRequest("stopLiveview", "");                // Send action request to camera host to stop liveview
                SonyQX10.imgStream.Close();
                SonyQX10.imgReader.Close();
                View_liveview(false);
                //Visualization("liveview", false);
            }
        }

        private void BShutterBtn_Click(object sender, EventArgs e)                              // Starts a shutter routine, calls Background Worker
        {
            if (SonyQX10.imgCount == 0)
            {
                if (!Directory.Exists(RootPath))                                                // Check requested directory exists, if not, creates it
                {
                    DirectoryInfo di = Directory.CreateDirectory(RootPath);
                }
            }

            SonyQX10.imgCount += 1;
            SonyQX10.SavePath = RootPath;
            SonyQX10.SaveName = ("P" + SonyQX10.imgCount.ToString("D4") + ".jpg");
            BShutterBtn.Enabled = false;
            BStateLbl.Text = "Taking picture...";
            SonyQX10.TakePicture.RunWorkerAsync();
            WriteReport("\r\nPicture " + SonyQX10.SaveName + "\r\nPicture shot at " + DateTime.Now.ToString("hh:mm:ss tt"));
        }

        private void guideRefreshBtn_Click(object sender, EventArgs e)                          // Loads image from live view to be frozen and displayed as a guide frame
        {
            ImgGuide.Location = new Point(0, 0);                                                // Ensures the reference image frame is in place
            Bitmap referenceImg;
            lock (locker)                                                                       // Calls lock on objects (necessary for avoiding issues on image load)
                referenceImg = new Bitmap(ImgLiveview.Image);
            TImage(referenceImg, GuideOpacityTB.Value);
        }

        private void guideChkBtn_CheckedChanged(object sender, EventArgs e)                     // Visualize frozen image to use it as a guide in liveview
        {
            View_guide(guideChkBtn.Checked);
        }

        private void resolutionChkBtn_CheckedChanged(object sender, EventArgs e)                // Changes generated image resolution
        {
            if (resolutionChkBtn.Checked)
                SonyQX10.CamResponse = SonyQX10.SendRequest("setPostviewImageSize", "\"Original\"");
            else
                SonyQX10.CamResponse = SonyQX10.SendRequest("setPostviewImageSize", "\"2M\"");
        }

        private void HPShutterChkBtn_CheckedChanged(object sender, EventArgs e)                 // Activates Half-Press Shutter action
        {
            if (HPShutterChkBtn.Checked)
                SonyQX10.CamResponse = SonyQX10.SendRequest("actHalfPressShutter");
            else
                SonyQX10.CamResponse = SonyQX10.SendRequest("cancelHalfPressShutter");
        }

        private void CommentBtn_Click(object sender, EventArgs e)                               // Adds an anytime comment on Report
        {
            string comment = PromptDialog.ShowDialog("Please type your comment:", "");
            WriteReport("\r\nComment (" + DateTime.Now.ToString("hh:mm:ss tt") + "): " + comment);
        }

        private void TImage(Bitmap referenceImg, int opacity)                                   // Loads a transparent image on the image guide picturebox
        {
            Bitmap transparentImg = new Bitmap(referenceImg.Width, referenceImg.Height);        // Aquires Image from Liveview
            Graphics tempG = Graphics.FromImage(referenceImg);
            Color c = Color.Transparent;
            Color v = Color.Transparent;
            for (int x = 0; x < referenceImg.Width; x++)                                        // Sweeps image pixels to change opacity
            {
                for (int y = 0; y < referenceImg.Height; y++)
                {
                    c = referenceImg.GetPixel(x, y);
                    v = Color.FromArgb(opacity, c.R, c.G, c.B);
                    transparentImg.SetPixel(x, y, v);
                }
            }
            tempG.DrawImage(transparentImg, Point.Empty);                                       // Loads Tranparent(ed) image on ImgGuide
            ImgGuide.Image = transparentImg;
        }

        void SonyQX10_ConnectEvent(object sender, ConnectEventArgs e)                           // Manages the events on cammera response
        {
            switch (e.ConnectionState)
            {
                case "LIVEVIEW":
                    if (!checkBox1.Checked)
                    {
                        try
                        {
                            if (SonyQX10.bmpImage != null)
                            {
                                ImgLiveview.Image = new Bitmap(SonyQX10.bmpImage);
                                SonyQX10.bmpImage.Dispose();
                                SonyQX10.bmpImage = null;
                                GC.Collect();
                                GC.WaitForPendingFinalizers();
                                if (!SonyQX10.LiveView.IsBusy)
                                    Invoke(new MethodInvoker(SonyQX10.LiveView.RunWorkerAsync));
                            }
                        }
                        catch (Exception)
                        {
                        }
                    }
                    else
                    {
                        liveviewAltBW.RunWorkerAsync();
                    }
                    break;
                case "UDP":
                    ConnectionTxt.Text = ("Status\r\n\r\nUDP-Socket setup finished...\r\n");
                    break;
                case "MSEARCH":
                    ConnectionTxt.AppendText("M-Search sent\r\n");
                    break;
                case "WAIT":
                    ConnectionTxt.AppendText("█");
                    break;
                case "CONNECTED":
                    ConnectionTxt.AppendText("\r\nConnection established\n");
                    break;
                case "SUCCESS":
                    ConnectionTxt.AppendText("\r\n\rConnection successful =)  \n");
                    getEventTxt.Text = SonyQX10.CamResponse;
                    //**************** Loads transparent logo to guide image (Done here to serve as a sleep function)
                    TImage(new Bitmap(ImgLogo.Image), 13);
                    ImgGuide.Location = new Point(0, 0);
                    //**************** Control Visualization *************
                    ConnectBtn.Text = ("Disconnect Camera");
                    View_camera(true);
                    View_capture(Arduino.ConSuc);
                    guideChkBtn.Enabled = false;
                    guideRefreshBtn.Enabled = false;
                    break;
                case "NOTCONNECTED":
                    ConnectBtn.Enabled = true;
                    ConnectionTxt.AppendText("\r\n\r\nFailed: Connection TimedOut =(  \n");
                    break;
                case "DISCONNECT":
                    ConnectBtn.Enabled = true;
                    ConnectionTxt.Visible = false;
                    ConnectBtn.Text = ("Connect Camera");
                    break;
                case "PICTURE":
                    BStateLbl.Text = ("Saving picture\r\n" + SonyQX10.SavePath + "\\" + SonyQX10.SaveName);
                    if (Auto & onCapture)
                    {
                        if (!unmanaged)
                            FocusPanel.Enabled = true;
                        if (onReCal)
                        {
                            BoardPanel.Enabled = true;
                            BoardAuxPanel.Enabled = true;
                            FocusPanel.Enabled = true;
                        }
                        if (myFrame == TotalFrames)                                                             // Frames, completed
                        {
                            onMove = true;
                            frameCount = TotalFrames;
                            Invoke(new EventHandler(ManageFrames));
                            break;
                        }
                        else
                        {
                            if (calibrationChkBtn.Checked)
                                Automation("capture", "servoStart");
                            else
                                Automation("capture", "move");
                        }
                    }

                    break;
                case "IMGSAVED":
                    BStateLbl.Text = ("Image saved.");
                    if (!Auto)
                    {
                        BShutterBtn.Enabled = true;
                        if (NoteChkBtn.Checked)
                        {
                            string comment = PromptDialog.ShowDialog("Please type the comments on this picture:", "");
                            WriteReport("Note: " + comment);
                        }
                    }
                    else
                    {
                        onSave = true;
                        Invoke(new EventHandler(ManageFrames));
                    }
                    break;

                default:
                    ConnectionTxt.AppendText(e.ConnectionState);
                    break;
            }
        }



        // These events are provided for test purposes only Any release: please leave these as NOT AVAILABLE (Or not visible). To enable test controls type TEST on the connection ComboBox (Same procedure to hide)
        //      TODO:

        private void getEventBtn_Click(object sender, EventArgs e)                              // Test Button (Not visible) Requests Events to camera
        {
            //*************** Shows JSON as string ******************************************
            //string json = SonyQX10.SendRequest("getEvent", "false", "1.1");
            ////var myjson = JsonConvert.DeserializeObject<string>(json);//JsonConvert.DeserializeObject<RootGetEvent>(json);
            //textBox1.Text = (json);
            ////************* Reads JSON format (Returns camera status) *********************
            string json = SonyQX10.SendRequest("getEvent", "false", "1.1");
            var myjson = JsonConvert.DeserializeObject<Camera.RootGetEvent>(json);
            textBox1.Text = ("");
            ////************* Visualizes JSON response **************************************
            for (i = 0; i < myjson.result.Count; i++)
            {
                if ((myjson.result[i]) != null)
                {
                    textBox1.AppendText("\r\n" + i + ")\r\n");
                    textBox1.AppendText(myjson.result[i].ToString());
                }
            }
            //************* Visualizes Camera status **************************************
            textBox2.Text = SonyQX10.ReadRequestJson(json, 10, 0, "numberOfRecordableImages"); //myjson.result[1]["cameraStatus"].ToString();
        }

        private void TestBtn_Click(object sender, EventArgs e)                                  // Test Button (Not available) <INSERT YOUR TEST CODE HERE>
        {
            //************* Freeze LiveView ********************************
            //SonyQX10.CanSend = !SonyQX10.CanSend;
            //************* Request DATA ***********************************
            Arduino.ReqInfo();

            //************* Request temperature ****************************
            //Arduino.RequTemperature();
            //WriteReport(Arduino.Temperature + "°C\r\n");
            //TemperatureTmr.Enabled = !TemperatureTmr.Enabled;
            //textBox1.Text = ("Temperature request: " + TemperatureTmr.Enabled.ToString());

            //************ Take picture ************************************
            //string CamResponse = SonyQX10.SendRequest("actTakePicture", "");

            //************ Check received info from board ******************
            //BStateLbl.Text = RxString;

            //************ Available functions *****************************
            //string json = SonyQX10.SendRequest("getAvailableApiList", " ", "1.0");
            //textBox1.Text = json;

            //************ Storage information *****************************
            //string json = SonyQX10.SendRequest("getStorageInformation", " ", "1.0");
            //textBox1.Text = json;

            //************ Zoom information ********************************
            //string json = SonyQX10.SendRequest("getEvent", "false", "1.1");
            //textBox2.Text = Convert.ToInt32(SonyQX10.ReadRequestJson(json, 2, "zoomPosition")).ToString();

            //************ Promt Dialog ************************************
            //string Prompt = PromptDialog.ShowDialog("Hello","There");
            //textBox2.Text = Prompt;

            //************ Show every panel in form ************************
            //foreach (Control panel in Controls)
            //{
            //    if (panel is Panel)
            //        panel.Visible = true;
            //}


        }

        private void pictureBox3_LoadCompleted(object sender, AsyncCompletedEventArgs e)        // Test image, load finished (Not loaded)
        {
            //if (OnCapture)
            //{
            //    BStateLbl.Text = (BStateLbl.Text + ("\nImage capture finished"));
            //    onSave = true;
            //    ManageFrames();
            //}
        }



        // The following code is (Mostly) related to the managing of the Board
        //              TODO: 

        private void BConnectionCBox_SelectedIndexChanged(object sender, EventArgs e)           // Enables connection button on serial type port selection (i.e. if a serial port is selecten in the combo box)
        {
            if (!Arduino.PortCOM.IsOpen)
            {
                if (BConnectionCBox.Text.Contains("COM"))
                {
                    BConnectBtn.Enabled = true;
                    Arduino.PortCOM.PortName = BConnectionCBox.Text;
                    Arduino.PortCOM.BaudRate = 115200;
                }
                else
                {
                    BConnectBtn.Enabled = false;
                    Arduino.PortCOM.PortName = " ";
                }
            }
        }

        private void BConnectionCBox_TextUpdate(object sender, EventArgs e)                     // Easter eggs for aditional setup 
        {
            if ((BConnectionCBox.Text == "RESET") && (Arduino.PortCOM.IsOpen))                  // Resets board connection
            {
                Arduino.StopSerial(sender, e);
                BConnectionCBox.Items.Clear();                                                          // Cleans previous data in Combobox
                BConnectionCBox.Items.Add("Select Port");
                BConnectionCBox.SelectedIndex = 0;
            }
            if (BConnectionCBox.Text == "TEST")                                                 // Enables/disables test panel visualization
            {
                TestControlPanel.Visible = !TestControlPanel.Visible;
                BConnectionCBox.Items.Clear();                                                          // Cleans previous data in Combobox
                BConnectionCBox.Items.Add("Select Port");
                BConnectionCBox.SelectedIndex = 0;
            }
            if (BConnectionCBox.Text.Contains("TIMER"))                                         // Modifyes time for connection timeout retry (#TIMER, where # is the number of miliseconds to elapse operation)
            {
                Arduino.boardTO = (Convert.ToInt16(BConnectionCBox.Text.Substring(0, (BConnectionCBox.Text.Length - 5))));
                BConnectionCBox.Items.Clear();                                                          // Cleans previous data in Combobox
                BConnectionCBox.Items.Add("Select Port");
                BConnectionCBox.SelectedIndex = 0;
            }
        }

        private void BConnectionCBox_DropDown(object sender, EventArgs e)                       // Sniffs for serial ports connected to the computer (Arduino connects vias Serial)
        {
            if (!Arduino.PortCOM.IsOpen)
            {
                string[] ports = SerialPort.GetPortNames();                                     // Sniffs for connected ports
                BConnectionCBox.Items.Clear();                                                  // Cleans previous data in Combobox
                BConnectionCBox.Items.Add("Select Port");
                BConnectionCBox.SelectedIndex = 0;
                foreach (string port in ports)                                                  // Adds available ports to the Combobox's list
                {
                    BConnectionCBox.Items.Add(port);
                }
            }
        }

        private void BConnectBtn_Click(object sender, EventArgs e)                              // Starts connection routine (No error handling)
        {
            Arduino.conTO = 0;
            BStateLbl.Text = ("Status");
            if (Arduino.PortCOM.PortName.Contains("COM"))                                       // Allows action if a valid COM port is connected/selected
            {
                if (!Arduino.PortCOM.IsOpen)                                                    // If port is closed, and a valid serial port is selected, allow connection
                {
                    Auto = false;                                      // Automated movement active flag
                    BoardData = false;                                 // Data change identifier flag
                    Calibrated = false;                                // Calibration routine flag
                    unmanaged = false;                                 // Unmanaged capture Flag
                    onCapture = false;                                 // Active capture flag   
                    onCalibration = false;                             // Active calibration flag        
                    onMove = false;                                    // Movement finished flag
                    onSave = false;                                    // Image saved flag
                    onAuto = false;                                    // Automation identifier flag
                    onReCal = false;                                   // Recalibration flag
                    BConnectBtn.Enabled = false;
                    BStateLbl.Visible = true;
                    Arduino.StartSerial();
                    session = Arduino.session;
                    // Monitor for the COMREQU command (visible on test panel)
                    getEventTxt.Text = Arduino.TxString + " Session ID: ";
                    getEventTxt.AppendText(BitConverter.ToString(Arduino.session));
                }
                else                                                                            // If port is open, close port (Manages controller labels)
                {
                    BStateLbl.Text = ("Disconnecting...");
                    foreach (Control control in Controls)
                    {
                        if (control is Panel)
                        {
                            control.Enabled = false;
                        }
                    }
                    Update();
                    BConnectBtn.Enabled = false;
                    Arduino.StopSerial(sender, e);
                }
            }
        }

        private void BSaveBtn_Click(object sender, EventArgs e)                                 // Saves data on calibration
        {
            Arduino.SaveData(BStepTxt.Text, BCycleTxt.Text, BTimeTxt.Text, BAStepTxt.Text);
            BoardData = true;
        }

        private void Connected(object sender, EventArgs e)                                      // Manages on connection actions. 
        {
            BStateLbl.Text = (BStateLbl.Text + "\nSession ID: " + BitConverter.ToString(Arduino.sessionRx));
            BStateLbl.Text = (BStateLbl.Text + "\nPort: " + Arduino.PortCOM.PortName.ToString() + "\n" + Arduino.PortCOM.BaudRate.ToString() + " bps\nConnection successful!!! :)");
            BConnectBtn.Text = ("Disconnect Board");
            View_board(true);
            Thread.Sleep(100);
            Arduino.ReqInfo();
            View_capture(SonyQX10.CamConStatus);
        }

        private void Disconnected(object sender, EventArgs e)                                   // Manages on connection actions.
        {
            BStateLbl.Text = ("Disconnected...");
            BConnectBtn.Text = ("Connect Board");
            foreach (Control control in Controls)
            {
                if (control is TextBox)
                    ((TextBox)control).Text = "";
            }
            View_board(false);
            View_capture(false);
        }

        private void FocusChkBox_CheckedChanged(object sender, EventArgs e)                     // Focus control enable/disable (Reset)
        {
            if (Arduino.ConSuc)
            {
                if (FocusChkBox.Checked)
                {
                    BStateLbl.Text += ("\r\nFocus enabled");
                    View_board(true, focusBool);
                }
                else
                {
                    Arduino.MoveServo(0);
                    focusTB.Value = 0;
                    BStateLbl.Text = ("Focus disabled");
                    View_board(false, focusBool);
                }
            }
        }

        private void focusTB_Scroll(object sender, EventArgs e)                                 // Controls the Servomotor position for focus knob handling 
        {
            if (!Arduino.Busy)
            {
                Arduino.MoveServo(focusTB.Value);
            }
        }

        private void TempChkBox_CheckedChanged(object sender, EventArgs e)                      // Temperature control enable/disabled 
        {
            if (Arduino.ConSuc)
            {
                if (TempChkBox.Checked)
                {
                    int temperature = (int)((TempValUpDown.Value * 1024) / 500);
                    Arduino.TempRef = (int)TempValUpDown.Value;
                    Arduino.SendTempRef(temperature);
                    BStateLbl.Text += ("\r\nTemperature control enabled");
                    View_board(true, tempBool);
                }
                else
                {
                    Arduino.SendTempRef(0);
                    TemperatureTmr.Enabled = false;
                    ChkTempBtn.Text = "Check";
                    BStateLbl.Text = ("Temperature control disabled");
                    View_board(false, tempBool);
                }
            }
            Thread.Sleep(200);
        }

        private void ChkTempBtn_Click(object sender, EventArgs e)                               // Starts temperature request (1 second each)
        {
            if (!TemperatureTmr.Enabled)
            {
                Arduino.RequTemperature();
                TempLbl.Text = (((int)Arduino.Temperature).ToString() + "°C");
                TemperatureTmr.Enabled = true;
                ChkTempBtn.Text = "Stop";
            }
            else
            {
                TemperatureTmr.Enabled = false;
                ChkTempBtn.Text = "Check";
            }
        }

        private void SetTempBtn_Click(object sender, EventArgs e)                               // Sets Temperature control (in Celsius)
        {
            int temperature = (int)((TempValUpDown.Value * 1024) / 500);
            Arduino.TempRef = (int)TempValUpDown.Value;
            Arduino.SendTempRef(temperature);
        }

        private void TemperatureTmr_Tick(object sender, EventArgs e)                            // Temperature Visualization timer 
        {
            Arduino.RequTemperature();
            TempLbl.Text = (((int)Arduino.Temperature).ToString() + "°C"); ;
        }

        //***************** Main motor ***********************************
        //              TODO: Organize comments

        private void MainMotorChkBox_CheckedChanged(object sender, EventArgs e)                 // Toggles activation state of the Main motor 
        {
            if (Arduino.ConSuc)
            {
                if (MainMotorChkBox.Checked)
                {
                    Arduino.Activate(Arduino.MainMotor);
                    Arduino.MainMotor.active = true;
                    View_capture(SonyQX10.CamConStatus);
                    Thread.Sleep(50);
                    BStateLbl.Text += ("\r\nMain motor activated");
                    if (AuxMotorChkBox.Checked)
                    {
                        Arduino.Activate(Arduino.AuxMotor);
                        Arduino.AuxMotor.active = true;
                        View_board(MainMotorChkBox.Checked, auxBool);
                        Thread.Sleep(50);
                        BStateLbl.Text += ("\r\nAuxiliar motor activated");
                    }
                }
                else
                {
                    Arduino.Deactivate(Arduino.MainMotor);
                    Arduino.MainMotor.active = false;
                    View_capture(false);
                    Thread.Sleep(50);
                    BStateLbl.Text = ("Main motor deactivated");
                    if (AuxMotorChkBox.Checked)
                    {
                        Arduino.Deactivate(Arduino.AuxMotor);
                        Arduino.AuxMotor.active = false;
                        View_board(MainMotorChkBox.Checked, auxBool);
                        Thread.Sleep(50);
                        BStateLbl.Text += ("\r\nAuxiliar motor deactivated");
                    }
                }
                View_board(MainMotorChkBox.Checked, mainBool);
                BoardAuxPanel.Enabled = MainMotorChkBox.Checked;
                AuxMotorChkBox.Enabled = MainMotorChkBox.Checked;
            }
        }

        private void BStepTB_Scroll(object sender, EventArgs e)                                 // Motor scroll function (TrackBar) 
        {

            Arduino.MainMotor.PosRef = BStepTB.Value;                                                                 // Stores user position of the Trackbar, this is the position reference to verify the stage movement
            if (!Arduino.Busy)                                                                              // Send data in execution timeif busy flag is false (When position is not fully attained, the program will check board reported position and stored position and send the difference)
            {
                Arduino.Busy = true;                                                                        // Sets busy flag
                BStepTBLbl.Text = ("Step (Main): " + Arduino.MainMotor.PosRef);                                              // Update position on visualization
                Arduino.MainMotor.Pos = BStepTB.Value;
                Arduino.MoveStage(ref Arduino.MainMotor, BStepTB.Value, 'P');                                                               // Request stage movement (managed by MoveStage function)
            }
        }

        private void BStepMinBtn_Click(object sender, EventArgs e)                              // Sends board request and sets current Trackbar position as Origin
        {
            Arduino.SetOrigin(ref Arduino.MainMotor);                                                       // Send Reset position board request
        }

        private void BStepMaxBtn_Click(object sender, EventArgs e)                              // Sets current Trackbar position as Max Step position
        {
            BStepTB.Maximum = BStepTB.Value;                                                        // Retrieves Trackbar current position
            BStepMaxLbl.Text = ("Max: " + BStepTB.Maximum);                                         // Updates position visualization
            BStateLbl.Text = ("Main motor maximum position\nSET");
        }

        private void BStepMax1Btn_Click(object sender, EventArgs e)                             // Diminishes step Max step on Trackbar
        {
            if (BStepTB.Maximum > 0)                                                        // No negative Value is accepted
            {
                if (BStepTB.Maximum == BStepTB.Value)                                       // In case the scroll value is at maximum value, then move scroll accordingly
                {
                    BStepTB.Value = BStepTB.Value - 1;                                      // Diminishes scroll value
                    BStepTB_Scroll(sender, e);                                              // NOT THE BEST PRACTICE: It calls the scroll "Scroll" action (Moves the motor and adjusts the form objects)
                }
                BStepTB.Maximum = BStepTB.Maximum - 1;                                      // Retrieves Trackbar current position
                BStepMaxLbl.Text = ("Max: " + BStepTB.Maximum);                             // Updates position visualization
                BStateLbl.Text = ("Main motor maximum position\nCHANGED");
            }
            else
            {
                BStepMax1Btn.Enabled = false;
            }
        }

        private void BStepMax2Btn_Click(object sender, EventArgs e)                             // Allows bigger step Max step on Trackbar
        {
            BStepMax1Btn.Enabled = true;
            BStepTB.Maximum = BStepTB.Maximum + 1;
            BStepMaxLbl.Text = ("Max: " + BStepTB.Maximum);
            BStateLbl.Text = ("Main motor maximum position\nSET");
        }

        private void BCycle1Btn_Click(object sender, EventArgs e)                               // Sends board request to move a complete cycle (Backwards)
        {
            if (!Arduino.Busy)
            {
                Arduino.Busy = true;                                                                        // Sets busy flag
                if (BCycleCountLbl.Text == "1")                                                     // Allow only positive movement
                {
                    BCycle1Btn.Enabled = false;
                }
                Arduino.MainMotor.Cycle -= 1;
                BCycleCountLbl.Text = Arduino.MainMotor.Cycle.ToString();
                Arduino.MoveStage(ref Arduino.MainMotor, Convert.ToInt32(BStepTxt.Text), 'S');                                     // Request cycle movement though MoveStage function
            }
        }

        private void BCycle2Btn_Click(object sender, EventArgs e)                               // Sends board request to move a complete cycle (Foward)
        {
            if (!Arduino.Busy)
            {
                Arduino.Busy = true;                                                                        // Sets busy flag
                if (BCycleCountLbl.Text == "0")                                                     // Enables for positive movement
                {
                    BCycle1Btn.Enabled = true;
                }
                Arduino.MainMotor.Cycle += 1;
                BCycleCountLbl.Text = Arduino.MainMotor.Cycle.ToString();
                Arduino.MoveStage(ref Arduino.MainMotor, Convert.ToInt32(BStepTxt.Text), 'Z');                                     // Request cycle movement though MoveStage function
            }
        }

        private void BCycleSetBtn_Click(object sender, EventArgs e)                             // Updates step setup to current step count
        {
            BCycleTxt.Text = BCycleCountLbl.Text;
            BStateLbl.Text = ("Main motor cycle\nSET");
        }

        private void BStepSetBtn_Click(object sender, EventArgs e)                              // Updates step setup to current Trackbar position
        {
            BStateLbl.Text = ("Main motor number of steps\nSET");
            BStepTxt.Text = BStepTB.Value.ToString();
        }

        private void BSpeedTB_Scroll(object sender, EventArgs e)                                // Sends board request for changing stage moving speed on execution time
        {
            if (!Arduino.Busy)
            {
                Arduino.ChangeSpeed(ref Arduino.MainMotor, BSpeedTB.Value);
            }
        }

        private void uStepChkBtn_CheckedChanged(object sender, EventArgs e)                     // Sends board request for uStep activation (Format and sends request depending case activation/deactivation)
        {
            Arduino.uStep(ref Arduino.MainMotor, uStepChkBtn.Checked);
        }

        private void reverseChkBtn_CheckedChanged(object sender, EventArgs e)                   // Sends board request for reverse activation (Format and sends request depending case forward/backwards)
        {
            Arduino.ChangeDirection(ref Arduino.MainMotor, reverseChkBtn.Checked);
        }

        private void BStepTxt_TextChanged(object sender, EventArgs e)                           // If the step value is changed, deactivates: data saved flag and calibration flag 
        {
            BoardData = false;
            Calibrated = false;
            calibrationChkBtn.Enabled = false;
        }

        private void BCycleTxt_TextChanged(object sender, EventArgs e)                          // If the cycle value is changed, deactivates: data saved flag and calibration flag 
        {
            BoardData = false;
            Calibrated = false;
            calibrationChkBtn.Enabled = false;
        }

        private void BTimeTxt_TextChanged(object sender, EventArgs e)                           // If the time value is changed, deactivates: data saved flag and calibration flag 
        {
            BoardData = false;
            //Calibrated = false;
            //calibrationChkBtn.Enabled = false;
        }

        //***************** Auxiliary motor ******************************
        //              TODO: Organize comments

        private void AuxMotorChkBox_CheckedChanged(object sender, EventArgs e)                  // Toggles activation state of the Auxiliary motor 
        {
            if (Arduino.ConSuc)
            {
                if (AuxMotorChkBox.Checked)
                {
                    Arduino.Activate(Arduino.AuxMotor);
                    Arduino.AuxMotor.active = true;
                    View_board(true, auxBool);
                    Thread.Sleep(50);
                    BStateLbl.Text += ("\r\nAuxiliar motor activated");
                }
                else
                {
                    Arduino.Deactivate(Arduino.AuxMotor);
                    Arduino.AuxMotor.active = false;
                    View_board(false, auxBool);
                    Thread.Sleep(50);
                    BStateLbl.Text = ("Auxiliar motor deactivated");
                }
            }
        }

        private void BAStepTB_Scroll(object sender, EventArgs e)                                // Motor scroll function (TrackBar)
        {
            Arduino.AuxMotor.PosRef = BAStepTB.Value;                                                                 // Stores user position of the Trackbar, this is the position reference to verify the stage movement
            textBox2.Text = BAStepTB.Value.ToString();
            if (!Arduino.Busy)                                                                              // Send data in execution timeif busy flag is false (When position is not fully attained, the program will check board reported position and stored position and send the difference)
            {
                Arduino.Busy = true;                                                                        // Sets busy flag
                BAStepTBLbl.Text = ("Step (Aux): " + Arduino.AuxMotor.PosRef);                                              // Update position on visualization
                Arduino.AuxMotor.Pos = BAStepTB.Value;
                Arduino.MoveStage(ref Arduino.AuxMotor, BAStepTB.Value, 'P');                                                               // Request stage movement (managed by MoveStage function)
            }
        }

        private void BAStepMinBtn_Click(object sender, EventArgs e)                             // Sends board request and sets current Trackbar position as Origin
        {
            Arduino.SetOrigin(ref Arduino.AuxMotor);                                                      // Send Reset position board request
        }

        private void BAStepMaxBtn_Click(object sender, EventArgs e)                             // Sets current Trackbar position as Max Step position
        {
            BAStepTB.Maximum = BAStepTB.Value;                                                        // Retrieves Trackbar current position
            BAStepMaxLbl.Text = ("Max: " + BAStepTB.Maximum);                                         // Updates position visualization
            BStateLbl.Text = ("Auxiliar motor maximum position\nSET");                                      // Updates position visualization
        }

        private void BAStepMax1Btn_Click(object sender, EventArgs e)                            // Diminishes step Max step on Trackbar
        {
            if (BAStepTB.Maximum > 0)                                                        // No negative Value is accepted
            {
                if (BAStepTB.Maximum == BAStepTB.Value)                                       // In case the scroll value is at maximum value, then move scroll accordingly
                {
                    BAStepTB.Value = BAStepTB.Value - 1;                                      // Diminishes scroll value
                    BAStepTB_Scroll(sender, e);                                              // NOT THE BEST PRACTICE: It calls the scroll "Scroll" action (Moves the motor and adjusts the form objects)
                }
                BAStepTB.Maximum = BAStepTB.Maximum - 1;                                      // Retrieves Trackbar current position
                BAStepMaxLbl.Text = ("Max: " + BAStepTB.Maximum);                             // Updates position visualization
                BStateLbl.Text = ("Auxiliar motor maximum position\nCHANGED");
            }
            else
            {
                BAStepMax1Btn.Enabled = false;
            }
        }

        private void BAStepMax2Btn_Click(object sender, EventArgs e)                            // Allows bigger step Max step on Trackbar
        {
            BAStepMax1Btn.Enabled = true;
            BAStepTB.Maximum = BAStepTB.Maximum + 1;
            BAStepMaxLbl.Text = ("Max: " + BAStepTB.Maximum);
            BStateLbl.Text = ("Auxiliar motor maximum position\nSET");
        }

        private void BACycle1Btn_Click(object sender, EventArgs e)                              // Sends board request to move a complete cycle (Backwards)
        {
            if (!Arduino.Busy)
            {
                Arduino.Busy = true;                                                                        // Sets busy flag
                if (BACycleCountLbl.Text == "1")                                                     // Allow only positive movement
                {
                    BACycle1Btn.Enabled = false;
                }
                Arduino.AuxMotor.Cycle -= 1;
                BACycleCountLbl.Text = Arduino.AuxMotor.Cycle.ToString();
                Arduino.MoveStage(ref Arduino.AuxMotor, Convert.ToInt32(BAStepTxt.Text), 'S');                                     // Request cycle movement though MoveStage function
            }
        }

        private void BACycle2Btn_Click(object sender, EventArgs e)                              // Sends board request to move a complete cycle (Foward)
        {
            if (!Arduino.Busy)
            {
                Arduino.Busy = true;                                                                        // Sets busy flag
                if (BACycleCountLbl.Text == "0")                                                     // Enables for positive movement
                {
                    BACycle1Btn.Enabled = true;
                }
                Arduino.AuxMotor.Cycle += 1;
                BACycleCountLbl.Text = Arduino.AuxMotor.Cycle.ToString();
                Arduino.MoveStage(ref Arduino.AuxMotor, Convert.ToInt32(BAStepTxt.Text), 'Z');                                     // Request cycle movement though MoveStage function
            }
        }

        private void BAStepSetBtn_Click(object sender, EventArgs e)                             // Updates step setup to current Trackbar position
        {
            BStateLbl.Text = ("Auxiliar motor number of steps\nSET");
            BAStepTxt.Text = BAStepTB.Value.ToString();
        }

        private void BASpeedTB_Scroll(object sender, EventArgs e)                               // Sends board request for changing stage moving speed on execution time
        {
            if (!Arduino.Busy)
            {
                Arduino.ChangeSpeed(ref Arduino.AuxMotor, BASpeedTB.Value);
            }
        }

        private void AuStepChkBtn_CheckedChanged(object sender, EventArgs e)                    // Sends board request for uStep activation (Format and sends request depending case activation/deactivation)
        {
            Arduino.uStep(ref Arduino.AuxMotor, AuStepChkBtn.Checked);
        }

        private void AreverseChkBtn_CheckedChanged(object sender, EventArgs e)                  // Sends board request for reverse activation (Format and sends request depending case forward/backwards)
        {
            Arduino.ChangeDirection(ref Arduino.AuxMotor, AreverseChkBtn.Checked);
        }

        private void BAStepTxt_TextChanged(object sender, EventArgs e)                          // If the auxiliar step value is changed, deactivates: data saved flag and calibration flag 
        {
            BoardData = false;
            Calibrated = false;
            calibrationChkBtn.Enabled = false;
        }

        //***************** Communication Action manager *****************
        //              TODO: 

        private void Arduino_Auxiliar(object sender, AuxiliarEventArgs e)                       // Manages Arduino instruction actions. When Arduino is processing any instruction, disables camera communication.
        {
            textBox2.Text += "\r\n" + e.Request;
            switch (e.Request)
            {
                case "RxOFF":
                    SonyQX10.CanSend = true;
                    break;
                case "RxON":
                    SonyQX10.CanSend = false;
                    break;
                case "Instruction":
                    textBox1.Text = ("\r\nReceived: " + Arduino.RxString + "\r\nSent: " + Arduino.TxString);
                    break;
                case "Repeat":
                    textBox1.Text = "\r\nInstruction Repeated";
                    break;
            }
        }

        private void Arduino_Instruction(object sender, InstructionEventArgs e)                 // Received the processed information from the board and deploys action
        {
            textBox2.Text += "\r\n" + e.ConStat;
            response = ("escape");
            string thisText = BStateLbl.Text;
            if (e.ConStat.Contains("error"))
            {
                string attempt = e.ConStat.Substring(5, (e.ConStat.Length - 5));
                BStateLbl.Text = ("Status\nAttempts: " + attempt);
                getEventTxt.Text = BitConverter.ToString(Arduino.TxByte);
                getEventTxt.AppendText(BitConverter.ToString(Arduino.session));
                textBox1.AppendText(BitConverter.ToString(Arduino.session));
            }
            switch (e.ConStat)
            {
                case "insFailed":
                    BStateLbl.Text = "Instruction error";
                    textBox1.Text = ("ERROR\r\n" + Arduino.TxString);
                    break;
                case "connected":
                    //Thread.Sleep(100);
                    if (!Arduino.PortCOM.IsOpen)
                    {
                        BConnectBtn.Enabled = true;
                        Arduino.ConSuc = false;
                        Arduino.PortSel = false;
                        Arduino.PortCOM.Dispose();
                        Invoke(new EventHandler(Disconnected));
                        MessageBox.Show("COM Port error");
                    }
                    else
                    {
                        session = Arduino.session;
                        BConnectBtn.Enabled = true;
                        Invoke(new EventHandler(Connected));
                    }
                    break;
                case "failed":
                    BStateLbl.Text = (BStateLbl.Text + "\nConnection Failed.\nTry to reconnect to the board...");
                    BConnectBtn.Enabled = true;
                    break;
                case "disconnect":
                    foreach (Control control in Controls)
                    {
                        if (control is Panel)
                        {
                            control.Enabled = true;
                        }
                    }
                    BConnectBtn.Enabled = true;
                    Invoke(new EventHandler(Disconnected));
                    break;
                case "Moving":
                    BStateLbl.Text = ("Moving " + e.Motor + " motor...");
                    break;
                case "MoveFinished":
                    BStateLbl.Text += ("\nMove Finished");
                    Arduino.Busy = false;
                    if (e.ID == '@')
                    {
                        BStepTBLbl.Text = ("Step (Main): " + Arduino.MainMotor.Pos);
                        if (Arduino.AuxMotor.active)
                            response = ("moveAux");
                        else
                            response = ("next");
                    }
                    if (e.ID == '~')
                    {
                        BAStepTBLbl.Text = ("Step (Aux): " + Arduino.AuxMotor.Pos);
                        response = ("next");
                    }
                    break;
                case "MoveIncomplete":
                    SonyQX10.CanSend = false;
                    if (e.ID == '@')
                    {
                        Arduino.MainMotor.PosRef = BStepTB.Value;
                        BStepTBLbl.Text = ("Step (Main): " + Arduino.MainMotor.PosRef);                                      // Moves stage if position has not been reached (particularly useful when movement is slow)
                        Update();
                    }
                    if (e.ID == '~')
                    {
                        Arduino.AuxMotor.PosRef = BAStepTB.Value;
                        BAStepTBLbl.Text = ("Step (Aux): " + Arduino.AuxMotor.PosRef);                                      // Moves stage if position has not been reached (particularly useful when movement is slow)
                        Update();
                    }
                    SonyQX10.CanSend = true;
                    break;
                case "DataInfo":
                    textBox2.Text += ("Main motor step: " + Arduino.MainMotor.StepVal + "\r\nMain motor cycle: " + Arduino.MainMotor.CycleVal + "\r\nCycle time: " + Arduino.MainMotor.TimeVal + "\r\nAuxiliar motor step: " + Arduino.AuxMotor.StepVal);
                    BStepTxt.Text = Arduino.MainMotor.StepVal;
                    BCycleTxt.Text = Arduino.MainMotor.CycleVal;
                    BTimeTxt.Text = Arduino.MainMotor.TimeVal;
                    BAStepTxt.Text = Arduino.AuxMotor.StepVal;
                    BoardData = true;
                    BStateLbl.Text = (BStateLbl.Text + ("\nData retrieved from board."));
                    if (onCalibration)
                    {
                        Array.Resize(ref Main, Convert.ToInt32(Arduino.MainMotor.CycleVal) + 1);
                        Array.Resize(ref Auxiliar, Convert.ToInt32(Arduino.MainMotor.CycleVal) + 1);
                        Array.Resize(ref FocusServo, Convert.ToInt32(Arduino.MainMotor.CycleVal) + 1);
                        Array.Resize(ref MainAct, Convert.ToInt32(Arduino.MainMotor.CycleVal) + 1);
                        Array.Resize(ref AuxiliarAct, Convert.ToInt32(Arduino.MainMotor.CycleVal) + 1);
                        for (i = 0; i <= Convert.ToInt32(Arduino.MainMotor.CycleVal); i++)
                        {
                            MainAct[i] = Convert.ToInt32(Arduino.MainMotor.StepVal) * i;
                            AuxiliarAct[i] = Convert.ToInt32(Arduino.AuxMotor.StepVal) * i;
                        }
                        request = "calibrate";
                        response = "start";
                    }
                    break;
                case "Origin":
                    BStateLbl.Text = ("Current " + e.Motor + " motor position set as origin");
                    if (e.ID == '@')
                    {
                        BStepTB.Value = 0;
                        BStepTBLbl.Text = ("Step (Main): 0");
                        BCycleCountLbl.Text = "0";
                        BCycle1Btn.Enabled = false;
                        response = ("originAux");

                    }
                    if (e.ID == '~')
                    {
                        BAStepTB.Value = 0;
                        BAStepTBLbl.Text = ("Step (Aux): 0");
                        BACycleCountLbl.Text = "0";
                        BACycle1Btn.Enabled = false;
                        response = ("none");
                    }
                    break;
                case "Cycle":
                    BStateLbl.Text = (e.Motor + " motor step finished");
                    if (e.ID == '@')
                    {
                        BStepTB.Value = 0;
                        BStepTBLbl.Text = ("Step (Main): 0");
                    }
                    if (e.ID == '~')
                    {
                        BAStepTB.Value = 0;
                        BAStepTBLbl.Text = ("Step (Aux): 0");
                    }
                    break;
                case "DataSaved":
                    BStateLbl.Text = ("Data Saved to board.");
                    if (Auto)
                        response = "folders";
                    if (onCalibration)
                    {
                        request = "calibrate";
                        response = "start";
                    }
                    break;
                case "ServoMoved":
                    BStateLbl.Text = ("Focus servo-motor moved.");
                    response = "servo";
                    break;
                case "temperature":
                    TempLbl.Text = (((int)Arduino.Temperature).ToString() + "°C");
                    break;
                default:
                    break;
            }
            if (Auto)
                Automation(request, response);
            if (onCalibration)
                Calibration(request, response);
            if (thisText.Contains("Cycle completed") & !BStateLbl.Text.Contains("Cycle completed"))
                BStateLbl.Text += ("\r\nCycle completed");
        }



        // The following code is (Mostly) related to Automated observation
        //      TODO:
        //              - Improve action visualization

        private void StartBtn_Click(object sender, EventArgs e)
        {
            if (!onAuto)
            {
                if ((BStepTB.Value != 0) | (BAStepTB.Value != 0) | (Convert.ToInt32(BCycleCountLbl.Text) != 0) | (Convert.ToInt32(BACycleCountLbl.Text) != 0))
                {
                    var result = MessageBox.Show("Current possition will be set as origin\r\nDo you want to continue?", "Confirm Automatic Observation", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                    if (result == DialogResult.Yes)
                    {
                        onAuto = true;                                                      // Automated observation -Global flag-
                        Auto = true;                                                        // Automated observation activity flag (is used to pause during automated operation, for example, when not automatically managing the capture)
                        onSave = false;                                                     // Image saved to PC flag
                        onMove = false;                                                     // Stage move complete flag
                        onCapture = false;                                                  // Picture aquisition flag (it is truewhen picture has been shot)

                        View_automated(false, "main");
                        Automation("start", "origin");
                    }
                    else
                    {
                        MessageBox.Show("Automated observation cancelled\r\nSet Origin manually and procceed",
                            "Set Origin Manually", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                        BStateLbl.Text = ("Automated observation cancelled\r\nSet Origin manually and procceed");
                    }
                }
                else
                {
                    Auto = true;
                    onAuto = true;
                    onSave = false;                                                     // Image saved to PC flag
                    onMove = false;                                                     // Stage move complete flag
                    onCapture = false;
                    View_automated(false, "main");
                    Automation("start");
                }
                Update();
            }
            else
            {
                Auto = false;
                onAuto = false;
                StartBtn.Text = "START";
                //timer1.Enabled = false;
                //IntervalTmr.Enabled = false;
                echoBW.CancelAsync();
                liveviewAltBW.CancelAsync();
                SonyQX10.TakePicture.CancelAsync();
                IntervalTmr.Enabled = false;
                timer1.Enabled = false;
                TemperatureTmr.Enabled = false;
                if (echoBW.IsBusy)
                    echoBW.CancelAsync();
                View_automated(false, "calibrate");
                View_automated(true, "main");
                Update();
                //if (calibrationChkBtn.Checked)
                BStateLbl.Text = ("Automated observation stopped\r\nIf any instruction is pending\r\nit will be executed");
                WriteReport("\r\nAutomated observation stopped at: " + DateTime.Now.ToString("hh:mm:ss tt"));
            }
        }

        private void CalibrationBtn_Click(object sender, EventArgs e)
        {
            Calibrated = false;
            if (CalibrationBtn.Text != "Calibrate")
                onCalibration = true;
            if (!onCalibration)
            {
                Array.Resize(ref Main, Convert.ToInt32(BCycleTxt.Text) + 1);
                Array.Resize(ref Auxiliar, Convert.ToInt32(BCycleTxt.Text) + 1);
                Array.Resize(ref FocusServo, Convert.ToInt32(BCycleTxt.Text) + 1);
                Array.Resize(ref MainAct, Convert.ToInt32(BCycleTxt.Text) + 1);
                Array.Resize(ref AuxiliarAct, Convert.ToInt32(BCycleTxt.Text) + 1);
                if (!BoardData)
                {
                    var result = MessageBox.Show("The current properties are not saved.\r\nDo you want to save the current configuration?" +
                        "\r\n(If you select NO, the last saved configuration will be loaded.)", "Current configuration not saved",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                    if (result == DialogResult.Yes)
                    {
                        for (i = 0; i <= Convert.ToInt32(BCycleTxt.Text); i++)
                        {
                            MainAct[i] = Convert.ToInt32(BStepTxt.Text) * i;
                            AuxiliarAct[i] = Convert.ToInt32(BAStepTxt.Text) * i;
                        }
                        Arduino.MainMotor.StepVal = MainAct.ToString();
                        Arduino.AuxMotor.StepVal = AuxiliarAct.ToString();
                        Arduino.SaveData(BStepTxt.Text, BCycleTxt.Text, BTimeTxt.Text, BAStepTxt.Text);
                    }
                    BoardData = true;
                }
                TotalFrames = Convert.ToInt32(Arduino.MainMotor.CycleVal);
                BStepTB.Maximum = TotalFrames * Convert.ToInt32(Arduino.MainMotor.StepVal);
                BStepMaxLbl.Text = ("Max: " + BStepTB.Maximum);
                myFrame = 0;
                if ((BStepTB.Value != 0) | (BAStepTB.Value != 0) | (Convert.ToInt32(BCycleCountLbl.Text) != 0) | (Convert.ToInt32(BACycleCountLbl.Text) != 0))
                {
                    var result = MessageBox.Show("Current possition will be set as origin\r\nDo you want to continue?", "Confirm calibration", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                    if (result == DialogResult.Yes)
                    {
                        onCalibration = true;
                        View_automated(false, "main");
                        Calibration("start", "origin");
                    }
                    else
                    {
                        MessageBox.Show("Calibration cancelled\r\nSet Origin manually and procceed",
                            "Set Origin Manually", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                        BStateLbl.Text = ("Calibration cancelled\r\nSet Origin manually and procceed");
                    }
                }
                else
                {
                    onCalibration = true;
                    View_automated(false, "main");
                    Calibration("start");
                }
            }
            else
            {

                FocusServo[myFrame] = focusTB.Value;
                Auxiliar[myFrame] = BAStepTB.Value;
                Main[myFrame] = BStepTB.Value;
                View_automated(false, "calibrate");
                if (myFrame < TotalFrames)
                    Calibration("calibrate", "start");
                else
                    Calibration("complete", "move");
            }
        }

        private void CaptureBtn_Click(object sender, EventArgs e)
        {
            if (!onCapture)
            {
                if (onReCal)
                {
                    FocusServo[myFrame] = focusTB.Value;
                    Auxiliar[myFrame] = BAStepTB.Value;
                    Main[myFrame] = BStepTB.Value;
                    FocusPanel.Enabled = false;
                }
                //View_automated(false, "main");
                BoardPanel.Enabled = false;
                BoardAuxPanel.Enabled = false;
                FocusPanel.Enabled = false;
                CaptureBtn.Enabled = false;
                onCapture = true;
                Auto = true;

                Automation("capture", "start");
            }
        }

        private void ManageChkBtn_CheckedChanged(object sender, EventArgs e)
        {
            unmanaged = ManageChkBtn.Checked;
        }

        private void calibrationChkBtn_CheckedChanged(object sender, EventArgs e)
        {
            ManageChkBtn.Checked = true;
            ManageChkBtn.Enabled = !calibrationChkBtn.Checked;
            if (calibrationChkBtn.Checked)
            {
                View_automated(true, "calibrate");
                CalibrationBtn.Enabled = false;
            }
            else
            {
                if (Arduino.ConSuc & SonyQX10.FlagLvw)
                {
                    CalibrationBtn.Enabled = false;
                    View_board(true);
                }
            }

        }

        private void reCalibrationChkBtn_CheckedChanged(object sender, EventArgs e)
        {
        }

        private void Automation(string instruction, string guide = "none")                      // Automation routine manager 
        {
            if (Auto)
            {
                request = instruction;
                if (guide == "escape")
                    instruction = "escape";
                switch (instruction)
                {
                    case "escape":
                        break;
                    case "start":
                        switch (guide)
                        {
                            case "none":
                            case "servo":
                                Invoke(new EventHandler(AutoSyncrhonize));
                                break;
                            case "origin":
                                Arduino.SetOrigin(ref Arduino.MainMotor);
                                break;
                            case "originAux":
                                Arduino.SetOrigin(ref Arduino.AuxMotor);
                                break;
                            case "save":
                                Arduino.SaveData(BStepTxt.Text, BCycleTxt.Text, BTimeTxt.Text, BAStepTxt.Text);
                                break;
                            case "folders":
                                Invoke(new EventHandler(CreateFolders));
                                break;
                            default:
                                break;
                        }
                        break;
                    case "prepare":
                        if (echoBW.IsBusy)
                            echoBW.CancelAsync();
                        progressBar2.Value = 0;
                        AuxTmrProg = (TotalTime / (TotalFrames + (TotalFrames / 4))) / 100;

                        switch (guide)
                        {
                            case "servoStart":
                                Thread.Sleep(5);
                                onSave = false;
                                onMove = false;
                                focusTB.Value = FocusServo[myFrame];
                                Arduino.MoveServo(FocusServo[myFrame]);
                                if (!echoBW.IsBusy)
                                    echoBW.RunWorkerAsync();
                                else
                                    echoTmr = 0;
                                break;
                            case "start":
                            case "servo":
                                Thread.Sleep(5);
                                View_automated(false, "main");
                                onSave = false;
                                onMove = false;
                                Arduino.MainMotor.PosRef = MainAct[myFrame];
                                BStepTB.Value = MainAct[myFrame];
                                BStepTBLbl.Text = ("Step (Main): " + MainAct[myFrame].ToString());
                                Arduino.MoveStage(ref Arduino.MainMotor, MainAct[myFrame], 'P');
                                if (!echoBW.IsBusy)
                                    echoBW.RunWorkerAsync();
                                else
                                    echoTmr = 0;
                                break;
                            case "moveAux":
                                Thread.Sleep(5);
                                Arduino.AuxMotor.PosRef = AuxiliarAct[myFrame];
                                BAStepTB.Value = AuxiliarAct[myFrame];
                                BAStepTBLbl.Text = ("Step (Aux): " + AuxiliarAct[myFrame].ToString());
                                Arduino.MoveStage(ref Arduino.AuxMotor, AuxiliarAct[myFrame], 'P');
                                if (!echoBW.IsBusy)
                                    echoBW.RunWorkerAsync();
                                else
                                    echoTmr = 0;
                                break;
                            case "next":
                                BStateLbl.Text = (BStateLbl.Text + ("\nAwaiting for capture"));
                                if (onReCal)
                                {
                                    onCapture = false;
                                    Auto = false;
                                    View_automated(true, "calibrate");
                                    Update();
                                }
                                if (onCapture)
                                    Automation("capture", "start");
                                break;
                            default:
                                break;
                        }
                        break;
                    case "capture":
                        if (echoBW.IsBusy)
                            echoBW.CancelAsync();
                        progressBar2.Value = 0;
                        AuxTmrProg = (TotalTime / (TotalFrames + (TotalFrames / 4))) / 100;
                        switch (guide)
                        {
                            case "start":
                                if (Auto)
                                {
                                    try
                                    {
                                        if (myFrame == 0)
                                            Invoke(new EventHandler(checktimer));
                                        else
                                            Invoke(new EventHandler(TakePictue));
                                    }
                                    catch (Exception ex)
                                    {
                                        Thread.Sleep(5);
                                        if (SonyQX10.TakePicture.IsBusy)
                                            SonyQX10.TakePicture.CancelAsync();
                                        Automation("capture", "start");
                                    }
                                }
                                break;
                            case "servoStart":
                                Thread.Sleep(5);
                                if (myFrame < TotalFrames)
                                    focusTB.Value = FocusServo[myFrame + 1];
                                Arduino.MoveServo(FocusServo[myFrame + 1]);
                                if (!echoBW.IsBusy)
                                    echoBW.RunWorkerAsync();
                                else
                                    echoTmr = 0;
                                break;
                            case "move":
                            case "servo":
                                if (myFrame < TotalFrames)
                                    Arduino.MainMotor.PosRef = MainAct[myFrame + 1];
                                BStepTB.Value = Arduino.MainMotor.PosRef;
                                BStepTBLbl.Text = ("Step (Main): " + Arduino.MainMotor.PosRef);
                                Arduino.MoveStage(ref Arduino.MainMotor, Arduino.MainMotor.PosRef, 'P');
                                if (!echoBW.IsBusy)
                                    echoBW.RunWorkerAsync();
                                else
                                    echoTmr = 0;
                                break;
                            case "moveAux":
                                if (myFrame < TotalFrames)
                                    Arduino.AuxMotor.PosRef = AuxiliarAct[myFrame + 1];
                                BAStepTB.Value = Arduino.AuxMotor.PosRef;
                                BAStepTBLbl.Text = ("Step (Aux): " + Arduino.AuxMotor.PosRef);
                                Arduino.MoveStage(ref Arduino.AuxMotor, Arduino.AuxMotor.PosRef, 'P');
                                if (!echoBW.IsBusy)
                                    echoBW.RunWorkerAsync();
                                else
                                    echoTmr = 0;
                                break;
                            case "next":
                                onMove = true;
                                Invoke(new EventHandler(ManageFrames));
                                break;
                            default:
                                break;
                        }
                        break;
                    case "complete":
                        if (echoBW.IsBusy)
                            echoBW.CancelAsync();
                        progressBar2.Value = 0;
                        AuxTmrProg = TotalTime / 350;
                        switch (guide)
                        {
                            case "servoStart":
                                Thread.Sleep(5);
                                focusTB.Value = FocusServo[0];
                                Arduino.MoveServo(FocusServo[0]);
                                if (!echoBW.IsBusy)
                                    echoBW.RunWorkerAsync();
                                else
                                    echoTmr = 0;
                                break;
                            case "move":
                            case "servo":
                                Arduino.MainMotor.PosRef = 0;
                                BStepTB.Value = 0;
                                BStepTBLbl.Text = ("Step (Main): 0");
                                Arduino.MoveStage(ref Arduino.MainMotor, 0, 'P');
                                if (!echoBW.IsBusy)
                                    echoBW.RunWorkerAsync();
                                else
                                    echoTmr = 0;
                                break;
                            case "moveAux":
                                Arduino.AuxMotor.PosRef = 0;
                                BAStepTB.Value = 0;
                                BAStepTBLbl.Text = ("Step (Aux): 0");
                                Arduino.MoveStage(ref Arduino.AuxMotor, 0, 'P');
                                if (!echoBW.IsBusy)
                                    echoBW.RunWorkerAsync();
                                else
                                    echoTmr = 0;
                                break;
                            case "next":
                                AuxTmrProg = (TotalTime / (TotalFrames + (TotalFrames / 4))) / 100;
                                myImg += 1;
                                myFrame = 0;
                                BStateLbl.Text += ("\nCycle completed");
                                View_automated(true, "calibrate");
                                Auto = false;
                                Arduino.RequTemperature();
                                if (onReCal)
                                    CompleteTime();
                                break;
                            default:
                                break;
                        }
                        break;
                }
            }
        }

        private void Calibration(string instruction, string guide = "none")
        {
            if (onCalibration)
            {
                request = instruction;
                if (guide == "escape")
                    instruction = "escape";
                switch (instruction)
                {
                    case "escape":
                        break;
                    case "start":
                        switch (guide)
                        {
                            case "none":
                                FocusServo[0] = focusTB.Value;
                                Auxiliar[0] = 0;
                                Main[0] = 0;
                                CapturePanel.Enabled = false;
                                Arduino.ReqInfo();
                                break;
                            case "origin":
                                Arduino.SetOrigin(ref Arduino.MainMotor);
                                break;
                            case "originAux":
                                Arduino.SetOrigin(ref Arduino.AuxMotor);
                                break;
                            default:
                                break;
                        }
                        break;
                    case "calibrate":
                        switch (guide)
                        {
                            case "start":
                                myFrame += 1;
                                Thread.Sleep(5);
                                Arduino.MainMotor.PosRef = MainAct[myFrame];
                                BStepTB.Value = Arduino.MainMotor.PosRef;
                                BStepTBLbl.Text = ("Step (Main): " + Arduino.MainMotor.PosRef);
                                Arduino.MoveStage(ref Arduino.MainMotor, Arduino.MainMotor.PosRef, 'P');
                                break;

                            case "next":
                            case "moveAux":
                                CalibrationBtn.Text = ("SET (" + myFrame.ToString() + ")");
                                View_automated(true, "calibrate");
                                onCalibration = false;
                                break;
                        }
                        break;
                    case "complete":
                        switch (guide)
                        {
                            case "move":
                                focusTB.Value = FocusServo[0];
                                Arduino.MoveServo(FocusServo[0]);
                                break;
                            case "servo":
                                Thread.Sleep(5);
                                Arduino.MainMotor.PosRef = 0;
                                BStepTB.Value = 0;
                                BStepTBLbl.Text = ("Step (Main): 0");
                                Arduino.MoveStage(ref Arduino.MainMotor, 0, 'P');
                                break;
                            case "moveAux":
                                Thread.Sleep(5);
                                Arduino.AuxMotor.PosRef = 0;
                                BAStepTB.Value = 0;
                                BAStepTBLbl.Text = ("Step (Aux): 0");
                                Arduino.MoveStage(ref Arduino.AuxMotor, 0, 'P');
                                break;
                            case "next":
                                onCalibration = false;
                                myFrame = 0;
                                Calibrated = true;
                                CalibrationBtn.Text = "Calibrate";
                                View_automated(true, "main");
                                CapturePanel.Enabled = true;
                                calibrationChkBtn.Checked = true;
                                break;
                            default:
                                break;
                        }
                        break;
                }
            }
        }

        private void AutoSyncrhonize(object sender, EventArgs e)
        {
            StartBtn.Text = "STOP";
            BStateLbl.Text = ("Synchronizing Configuration...");
            TotalFrames = Convert.ToInt32(BCycleTxt.Text);
            TotalTime = Convert.ToInt32(BTimeTxt.Text) * 1000;
            timer1.Interval = TotalTime / 100;
            IntervalTmr.Interval = TotalTime;
            progressBar1.Value = 0;
            myFrame = 0;
            myImg = 0;
            Array.Resize(ref MainAct, Convert.ToInt32(BCycleTxt.Text) + 1);
            Array.Resize(ref AuxiliarAct, Convert.ToInt32(BCycleTxt.Text) + 1);
            if (Calibrated & calibrationChkBtn.Checked)
            {
                BStepTB.Maximum = Main.Max();
                WriteReport("Automated observation started at: " + DateTime.Now.ToString("hh:mm:ss tt") +
                    "\r\nProgrammed Steps per cycle: " + BStepTxt.Text + " (Values Calibrated)" +
                    "\r\nNumber of cycles: " + BCycleTxt.Text +
                    "\r\nTime interval (Seconds): " + BTimeTxt.Text +
                    "\r\nCalibration values assigned");
                MainAct = Main;
                AuxiliarAct = Auxiliar;
                reCalibrationChkBtn.Enabled = true;
                ManageChkBtn.Checked = true;
                ManageChkBtn.Enabled = false;
            }
            else
            {
                BStepTB.Maximum = TotalFrames * Convert.ToInt32(BStepTxt.Text);
                WriteReport("Automated observation started at: " + DateTime.Now.ToString("hh:mm:ss tt") +
                    "\r\nSteps per cycle: " + BStepTxt.Text +
                    "\r\nNumber of cycles: " + BCycleTxt.Text +
                    "\r\nTime interval (Seconds): " + BTimeTxt.Text);
                for (i = 0; i <= Convert.ToInt32(BCycleTxt.Text); i++)
                {
                    MainAct[i] = Convert.ToInt32(BStepTxt.Text) * i;
                    AuxiliarAct[i] = Convert.ToInt32(BAStepTxt.Text) * i;
                }
            }
            BStepMaxLbl.Text = ("Max: " + BStepTB.Maximum);
            if (Arduino.AuxMotor.active)
                WriteReport("Auxiliar motor: Enabled");
            Automation("start", "save");
        }

        private void CreateFolders(object sender, EventArgs e)
        {
            BStateLbl.Text = (BStateLbl.Text + ("\nCreating Folders..."));
            PicPath = (RootPath + "\\Session" + BitConverter.ToString(session));
            i = 1;
            while (Directory.Exists(PicPath))                                                            // Check requested directory exists, if not, creates it
            {
                PicPath = (RootPath + "\\Session" + BitConverter.ToString(session) + ("_") + i.ToString("D2"));
                i += 1;
            }
            if (!Directory.Exists(PicPath))
            {
                DirectoryInfo di = Directory.CreateDirectory(PicPath);
                WriteReport("Pictures saved in: " + PicPath);
            }
            for (i = 0; i <= Convert.ToInt32(TotalFrames); i++)
            {
                if (!Directory.Exists(PicPath + "\\Frame" + i.ToString("D4")))
                {
                    DirectoryInfo di = Directory.CreateDirectory(PicPath + "\\Frame" + i.ToString("D4"));
                }
            }
            myFrame = 0;
            myImg = 0;

            if (unmanaged)
            {
                onCapture = true;
                if (calibrationChkBtn.Checked)
                    Automation("prepare", "servoStart");
                else
                    Automation("prepare", "start");
            }
            else
            {
                BStateLbl.Text = (BStateLbl.Text + ("\nAwaiting for capture"));
                Auto = false;
                CaptureBtn.Enabled = true;
                if (onReCal)
                {
                    //FocusPanel.Enabled = true;
                    onCapture = false;
                }
            }
        }

        private void TakePictue(object sender, EventArgs e)
        {
            BStateLbl.Text = ("Frame: " + myFrame.ToString() + " Cycle: " + myImg.ToString() + ("\nCapturing..."));
            SonyQX10.SaveName = (("S") + BitConverter.ToString(session) + ("F") + myFrame.ToString("D3") + ("P") + myImg.ToString("D3") + ".jpg");
            SonyQX10.SavePath = (PicPath + "\\Frame" + myFrame.ToString("D4"));
            SonyQX10.TakePicture.RunWorkerAsync();
        }

        private void ManageFrames(object sender, EventArgs e)
        {
            if (onMove & onSave)
            {
                if (myFrame < TotalFrames)
                {
                    myFrame += 1;
                    onMove = false;
                    onSave = false;
                    BStateLbl.Text = (BStateLbl.Text + ("\nAwaiting for capture"));
                    if (unmanaged)
                    {
                        if (calibrationChkBtn.Checked)
                            Automation("prepare", "servoStart");
                        else
                            Automation("prepare", "start");
                    }
                    else
                    {
                        Auto = false;
                        //BoardPanel.Enabled = true;
                        //BoardAuxPanel.Enabled = true;
                        //CameraPanel.Enabled = true;
                        onCapture = false;
                        CaptureBtn.Enabled = true;
                        if (onReCal)
                        {
                            FocusPanel.Enabled = true;
                        }
                    }
                }
                else
                {
                    onMove = false;
                    onSave = false;
                    onCapture = false;
                    CaptureBtn.Enabled = false;
                    if (calibrationChkBtn.Checked)
                        Automation("complete", "servoStart");
                    else
                        Automation("complete", "move");
                }
            }
        }


        private void ProtectControls(ref Panel thisPanel, bool show)
        {
            string[] protectedControl = { "TB", "Img", "StepChkBtn", "reverseChkBtn", "Max1Btn", "Max2Btn", "StepMaxLbl" };
            foreach (Control control in thisPanel.Controls)
            {
                if (!(protectedControl.Any(control.Name.Contains)))
                    control.Enabled = show;
            }
            Update();
        }

        private void checktimer(object sender, EventArgs e)
        {
            if (!onReCal)
            {
                IntervalTmr.Enabled = true;
                timer1.Enabled = true;
                progressBar1.Visible = true;
            }
            Invoke(new EventHandler(TakePictue));
        }


        private void timer1_Tick(object sender, EventArgs e)                                    // Progress bar timer (Manages time visualization)
        {
            if (progressBar1.Value < 100)
                progressBar1.Value += 1;
        }

        private void IntervalTmr_Tick(object sender, EventArgs e)
        {
            CompleteTime();
        }

        private void CompleteTime()
        {
            IntervalTmr.Enabled = false;
            timer1.Enabled = false;
            progressBar1.Visible = false;
            if (!calibrationChkBtn.Checked)
                ManageChkBtn.Enabled = true;
            progressBar1.Value = 0;
            unmanaged = ManageChkBtn.Checked;
            if (reCalibrationChkBtn.Checked)
            {
                if (!onReCal)
                {
                    ManageChkBtn.Enabled = false;
                    unmanaged = false;
                    reCalibrationChkBtn.Enabled = false;
                    onReCal = true;
                }
                else
                {
                    onReCal = false;
                    reCalibrationChkBtn.Enabled = true;
                    reCalibrationChkBtn.Checked = false;
                }
            }
            if (!BStateLbl.Text.Contains("Cycle completed"))
            {
                if (!checkBox2.Checked)
                {
                    if (SonyQX10.TakePicture.IsBusy)
                        SonyQX10.TakePicture.CancelAsync();
                    Auto = false;
                    IntervalTmr.Enabled = false;
                    timer1.Enabled = false;
                    StartBtn.Text = "START";
                    View_automated(true, "calibrate");
                    //ProtectControls(ref BoardPanel, true);
                    //ProtectControls(ref BoardAuxPanel, true);
                    //BoardPanel.Enabled = true;
                    //BoardAuxPanel.Enabled = true;
                    //CameraPanel.Enabled = true;
                    //progressBar1.Visible = false;
                    BStateLbl.Text = ("Automated observation cancelled\r\nNot enough time to complete cycle\r\nIf any instruction is pending\r\nit will be executed");
                    WriteReport("\r\nAutomated observation cancelled due to lack of time at: " + DateTime.Now.ToString("hh:mm:ss tt"));
                    MessageBox.Show("Automated observation cannot complete cycle\r\nPlease adjust time and restart Observation", "Insufficient time for automated observation", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                else
                {
                    echoBW.CancelAsync();
                    liveviewAltBW.CancelAsync();
                    SonyQX10.TakePicture.CancelAsync();
                    IntervalTmr.Enabled = false;
                    TemperatureTmr.Enabled = false;
                    myImg += 1;
                    myFrame = 0;
                    onMove = false;
                    onSave = false;

                    WriteReport("\r\nThis cycle was NOT completed (Cycle:" + myImg.ToString() + ")\r\n" + DateTime.Now.ToString("hh:mm:ss tt") + "\r\nCycle " + myImg.ToString() + ", Temperature: " + Arduino.Temperature.ToString() + "°C\r\n");
                    BStateLbl.Text = "Cycle incomplete\r\nTime Cycled completed, resuming observation.";
                    Update();

                    Auto = true;
                    if (!unmanaged)
                    {
                        SystemSounds.Exclamation.Play();
                        CaptureBtn.Enabled = true;
                        onCapture = false;
                    }
                    else
                    {
                        SystemSounds.Beep.Play();
                        onCapture = true;
                    }
                    if (calibrationChkBtn.Checked)
                        Automation("prepare", "servoStart");
                    else
                        Automation("prepare", "start");
                }
            }
            else
            {
                WriteReport(DateTime.Now.ToString("hh:mm:ss tt") + "\r\nCycle " + myImg.ToString() + ", Temperature: " + Arduino.Temperature.ToString() + "°C\r\n");
                if (onReCal)
                {
                    BStateLbl.Text = ("RECALIBRATION ROUTINE IN PROGRESS...");
                    WriteReport("Recalibration was performed");
                }
                Auto = true;
                if (!unmanaged)
                {
                    SystemSounds.Exclamation.Play();
                    CaptureBtn.Enabled = true;
                    onCapture = false;
                }
                else
                {
                    SystemSounds.Beep.Play();
                    onCapture = true;
                }
                if (calibrationChkBtn.Checked)
                    Automation("prepare", "servoStart");
                else
                    Automation("prepare", "start");
            }
        }

        private void Repeat_Click(object sender, EventArgs e)
        {
            if (Arduino.PortCOM.IsOpen)
                Arduino.PortCOM.Write("REPEAT");
        }


        private void echoBW_DoWork(object sender, DoWorkEventArgs e)
        {
            for (echoTmr = 0; echoTmr <= 100; echoTmr++)
            {
                echoBW.ReportProgress(echoTmr);
                //Application.DoEvents();
                Thread.Sleep(AuxTmrProg);
            }
        }

        private void echoBW_ProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            progressBar2.Value = Convert.ToInt32(e.ProgressPercentage);
            //textBox2.Text = ("Repeat in " + progressBar2.Value.ToString());
        }

        private void echoBW_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            if (Arduino.PortCOM.IsOpen)
                Arduino.PortCOM.Write("REPEAT");
            //textBox2.Text = "REPEAT";
            progressBar2.Value = 0;

        }

        private void liveviewAltBW_DoWork(object sender, DoWorkEventArgs e)
        {
            try
            {
                if (SonyQX10.bmpImage != null)
                {
                    lock (locker)
                    {
                        ImgLiveview.Image = SonyQX10.bmpImage;
                        SonyQX10.bmpImage = null;
                    }
                }
            }
            catch (Exception ex)
            {
                throw ex;
            }
        }
    }
}


