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

namespace Microscope_Control
{
    public delegate void ConnectEventHandler(object sender, ConnectEventArgs e);
    public class ConnectEventArgs : EventArgs
    {
        public string ConnectionState { get; set; }
    }

    class Camera
    {

        public class RootGetEvent
        {
            public int id { get; set; }
            public JArray result { get; set; }
        }


        //  Public Variable definition

        public bool onSave = false;
        public bool SaveQuery = true;
        public bool CamConStatus = false;                           // Camera connection flag
        public bool FlagLvw = false;                                // Flag to retrieve action on liveview event
        public int imgCount = 0;
        public string lvwURL = "";                                  // Stores camera URL for liveview
        public string SavePath;
        public string SaveName;
        public string CamResponse = "";                             // Retrieves the camera response when any action is invoked
        public bool CanSend = true;
        
                    
        public event ConnectEventHandler ConnectEvent;
        ConnectEventArgs args = new ConnectEventArgs();
        protected virtual void OnConnectEvent(ConnectEventArgs e)
        {
            ConnectEvent?.Invoke(this, e);
        }


        // Connection Data
        private Socket UdpSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);  // Creates Socket for managing network communication
        // LiveView Data
        private List<byte> imgData = new List<byte>();                                                          // Byte list for storing image data
        private int imgSize = 0;                                                                                // Image size for data retrieval (Liveview)
        private byte[] buffer = new byte[520];                                                                  // Data buffer for liveview
        private byte[] bufferAux = new byte[4];                                                                 // Data auxiliar buffer for liveview
        private byte payloadType = 0;                                                                           // Stores the payload type from liveview stream
        private int frameNo = 0;                                                                                // Frame No. (Liveview)
        private int paddingSize = 0;                                                                            // Padding size (Liveview)
        public Stream imgStream;                                                                                // Data stream for image aquisition (Liveview)
        public StreamReader imgReader;                                                                          // Stream reader for image data (Liveview)
        public Bitmap bmpImage;
        // Background workers definition
        public BackgroundWorker Connect = new BackgroundWorker();
        public BackgroundWorker Disconnect = new BackgroundWorker();
        public BackgroundWorker LiveView = new BackgroundWorker();
        public BackgroundWorker TakePicture = new BackgroundWorker();

        public string SendRequest(string method, string param = (""), string version = ("1.0"))//params string[] data)                                         // Gives format to the action request, manages sending request and receiving response. Output: Response JSON string
        {
            //Array.Resize(ref data, 3);                                                                              // Arrange input data (Arranges a 3-item array)
            //string method = data[0];                                                                                // Sets default values for parameters and version
            //string param = ("");
            //string version = ("1.0");
            //if (data[1] != null)                                                                                    // Assigns input values (If any)
            //{
            //    param = data[1];
            //}
            //if (data[2] != null)
            //{
            //    version = data[2];
            //}
            string responseF;                                                                                       // String for storing camera response (Return)
            try
            {
                // Create POST data and convert it to a byte array (Set the ContentType property of the WebRequest to an 8-bit Unicode). Data is not Serialized to JSON due that params (required property) is a C# keyword
                string postData = "{\"method\": \"" + method + "\",\"params\": [" + param + "],\"id\": 1,\"version\": \"" + version + "\"}";
                byte[] byteArray = Encoding.UTF8.GetBytes(postData);

                // Send action request
                WebRequest request = WebRequest.Create("http://10.0.0.1:10000/sony/camera ");                       // Create a request using the camera Action list URL
                request.Method = "POST";                                                                            // Set the Method property of the request to POST
                request.ContentType = "application/json; charset=utf-8";                                            // Set the request content type to match JSON encoding
                request.ContentLength = byteArray.Length;                                                           // Set the ContentLength property of the WebRequest
                Stream dataStream = request.GetRequestStream();                                                     // Get the request stream
                dataStream.Write(byteArray, 0, byteArray.Length);                                                   // Write the data to the request stream
                dataStream.Close();                                                                                 // Close the Stream object

                // Receive camera (Host) response
                WebResponse response = request.GetResponse();                                                       // Display the status
                //ConnectionTxt.AppendText(((HttpWebResponse)response).StatusDescription);
                dataStream = response.GetResponseStream();                                                          // Open the stream using a StreamReader for easy access
                StreamReader reader = new StreamReader(dataStream);
                string responseFromServer = reader.ReadToEnd();
                //ConnectionTxt.AppendText(responseFromServer);

                // Close Objects
                reader.Close();                                                                                     // Closes reader, stream object and response
                dataStream.Close();
                response.Close();
                responseF = responseFromServer;
            }
            catch (Exception)
            {
                return ("");
            }
            return responseF;
        }

        public string ReadRequestJson(string json, int order, string key, int item)             // Reads JSON format and returns specified property: 
        {
            RootGetEvent myjson = JsonConvert.DeserializeObject<RootGetEvent>(json);
            string property = myjson.result[order][key][item].ToString();
            return property;
        }
        public string ReadRequestJson(string json, int order, int item, string key)             // Reads JSON format and returns specified property: 
        {
            RootGetEvent myjson = JsonConvert.DeserializeObject<RootGetEvent>(json);
            string property = myjson.result[order][item][key].ToString();
            return property;
        }
        public string ReadRequestJson(string json, int order, string key)                       //      Uses the JSON string, the order number and the string key (Ref. Sony remote camera API reference document) 
        {
            RootGetEvent myjson = JsonConvert.DeserializeObject<RootGetEvent>(json);
            string property = myjson.result[order][key].ToString();
            return property;
        }
        public string ReadRequestJson(string json, int order, int key)                          //      Uses the JSON string, the order number and number key (Ref. Sony remote camera API reference document)
        {
            RootGetEvent myjson = JsonConvert.DeserializeObject<RootGetEvent>(json);
            string property = myjson.result[order][key].ToString();
            return property;
        }
        public string ReadRequestJson(string json, int order)                                   //      Uses the JSON string and the order number (Ref. Sony remote camera API reference document)
        {
            RootGetEvent myjson = JsonConvert.DeserializeObject<RootGetEvent>(json);
            string property = myjson.result[order].ToString();
            return property;
        }
        public string ReadRequestJson(string json)                                              //      Uses only the JSON string (Ref. Sony remote camera API reference document)
        {
            RootGetEvent myjson = JsonConvert.DeserializeObject<RootGetEvent>(json);
            string property = myjson.result.ToString();
            return property;
        }
        public int CountRequestJson(string json)                                                // Returns number of parameters in JSON string
        {
            RootGetEvent myjson = JsonConvert.DeserializeObject<RootGetEvent>(json);
            int property = myjson.result.Count;
            return property;
        }

        public Camera()                                                          // Initializes background worker events
        {
            Connect.DoWork += Connect_DoWork;
            Disconnect.DoWork += Disconnect_DoWork;
            LiveView.DoWork += LiveView_DoWork;
            TakePicture.DoWork += TakePicture_DoWork;
            TakePicture.RunWorkerCompleted += TakePicture_RunWorkerCompleted1;
            LiveView.WorkerSupportsCancellation = true;
            TakePicture.WorkerSupportsCancellation = true;                       // Create a request using the camera Action list URL

        }

        private void StatSender(string status)
        {
            if (CanSend)
            {
                args.ConnectionState = status;
                OnConnectEvent(args);
            }
        }

        private void Connect_DoWork(object sender, DoWorkEventArgs e)                           // Manages the discovery routine to connect with camera DSC-QX10 (Must be connected to PC WiFi)
        {
            try
            {
                // Setup Client/Host Endpoints and communication socket
                // check for only connection to Wifi
                IPAddress ipAddress = IPAddress.Any;
                foreach (NetworkInterface adapter in NetworkInterface.GetAllNetworkInterfaces())
                {
                    var ipProps = adapter.GetIPProperties();
                    if (adapter.NetworkInterfaceType == NetworkInterfaceType.Wireless80211)
                    {
                        foreach (var ip in ipProps.UnicastAddresses)
                        {
                            if ((adapter.OperationalStatus == OperationalStatus.Up)
                                && (ip.Address.AddressFamily == AddressFamily.InterNetwork))
                            {
                                if (ipAddress != null)
                                    ipAddress = ip.Address;
                                break;
                            }
                        }
                    }
                }
                IPEndPoint LocalEndPoint = new IPEndPoint(ipAddress, 8888);

                //IPEndPoint LocalEndPoint = new IPEndPoint(IPAddress.Any, 60000);                                        // Creates Endpoint to connect with system client
                IPEndPoint MulticastEndPoint = new IPEndPoint(IPAddress.Parse("239.255.255.250"), 1900);                // Creates Endpoint to connect with camera host (Multicast messages reserved address, Sony SDK)
                UdpSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);          // Creates Socket for managing network communication
                UdpSocket.Bind(LocalEndPoint);                                                                          // Asociates Local socket to external host (Camera)
                StatSender("UDP");

                // Sends discovery request to camera host (SSDP M-SEARCH)
                string SearchString = "M-SEARCH * HTTP/1.1\r\nHOST:239.255.255.250:1900\r\nMAN:\"ssdp:discover\"\r\nMX:2\r\nST:urn:schemas-sony-com:service:ScalarWebAPI:1\r\n\r\n";
                
                // SSDP M-SEARCH request (SONY SDK) string
                UdpSocket.SendTo(Encoding.UTF8.GetBytes(SearchString), SocketFlags.None, MulticastEndPoint);            // Sends M-SEARCH request (8-bit Unicode) UNICAST
                StatSender("MSEARCH");

                // Receives discovery response from camera UNICAST (TimedOut on 10 secs)
                byte[] ReceiveBuffer = new byte[64000];
                int ReceivedBytes = 0;
                Thread TimeoutThread = new Thread(ThreadProc);
                TimeoutThread.Start();
                int i = 0;
                while (TimeoutThread.IsAlive)                                                                                            // Received Buffered response
                {
                    i += 1;
                    if (i % 950000 == 0)
                    {
                        StatSender("WAIT");
                    }
                    if (UdpSocket.Available > 0)
                    {
                        ReceivedBytes = UdpSocket.Receive(ReceiveBuffer, SocketFlags.None);
                        StatSender("CONNECTED");
                        CamConStatus = true;

                        if (ReceivedBytes > 0)
                        {
                            StatSender(Encoding.UTF8.GetString(ReceiveBuffer, 0, ReceivedBytes));
                        }
                        break;
                    }
                }
                TimeoutThread.Abort();
                if (CamConStatus)
                {
                    // Setups form objects OnConnect, Zooms camera in and sends request toreceive full resolution images.
                    CamResponse = SendRequest("setPostviewImageSize", "\"Original\"");
                    CamResponse = SendRequest("actZoom", "\"in\",\"start\"");
                    CamConStatus = true;
                    StatSender("SUCCESS");

                }
                else
                {
                    CamConStatus = false;
                    UdpSocket.Close();
                    StatSender("NOTCONNECTED");
                }
            }
            catch (Exception)
            {
                //throw ex;
                //MessageBox.Show(ex.Message);
            }
        }

        private void Disconnect_DoWork(object sender, DoWorkEventArgs e)                        // Manages the disconnection routine of the camera DSC-QX10
        {
            CamConStatus = false;
            ConnectEventArgs args = new ConnectEventArgs();
            CamResponse = SendRequest("actZoom", "\"out\",\"start\"");
            int zoom = -1;
            while (zoom != 0)
            {
                string json = SendRequest("getEvent", "false", "1.1");
                zoom = Convert.ToInt32(ReadRequestJson(json, 2, "zoomPosition"));
            }
            UdpSocket.Close();
            StatSender("DISCONNECT");
        }

        private void LiveView_DoWork(object sender, DoWorkEventArgs e)                          // Request liveview image, reads and stores image 
        {
            BackgroundWorker bw = sender as BackgroundWorker;
            ConnectEventArgs args = new ConnectEventArgs();
            try
            {
                while (!bw.CancellationPending)
                {
                    using (var memstream = new MemoryStream())
                    {
                        imgData = new List<byte>();
                        imgSize = 0;
                        buffer = new byte[520];
                        bufferAux = new byte[4];
                        payloadType = 0;
                        frameNo = -1;
                        paddingSize = 0;

                        GetHeader:                                                          // Retrieves a byte(s) from the stream to check if it corresponds to Sony header construction

                        // Common Header (8 Bytes)
                        imgReader.BaseStream.Read(buffer, 0, 1);                            // Seeks for start byte
                        var start = buffer[0];
                        if (start != 0xff)
                            goto GetHeader;

                        imgReader.BaseStream.Read(buffer, 0, 1);                            // Stores payload Type
                        payloadType = (buffer[0]);
                        if (!((payloadType == 1) || (payloadType == 2)))
                            goto GetHeader;

                        imgReader.BaseStream.Read(buffer, 0, 2);                            // Stores Frame Number depending Payload type
                        if (payloadType == 1)
                            frameNo = BitConverter.ToUInt16(buffer, 0);

                        imgReader.BaseStream.Read(buffer, 0, 4);                            // Discards expected Time stamp

                        // Payload header (128 bytes)
                        imgReader.BaseStream.Read(buffer, 0, 4);
                        if (!((buffer[0] == 0x24) & (buffer[1] == 0x35) & (buffer[2] == 0x68) & (buffer[3] == 0x79)))
                            goto GetHeader;                                                 // If the start code does not correspond to fixed code (0x24, 0x35, 0x68, 0x79), starts over

                        imgReader.BaseStream.Read(bufferAux, 0, 4);
                        paddingSize = bufferAux[3];
                        bufferAux[3] = bufferAux[2];
                        bufferAux[2] = bufferAux[1];
                        bufferAux[1] = bufferAux[0];
                        bufferAux[0] = 0;
                        Array.Reverse(bufferAux);
                        imgSize = BitConverter.ToInt32(bufferAux, 0);                       // Reads and translates Data stream size

                        if (payloadType == 1)                                               // Case JPEG data
                        {
                            imgReader.BaseStream.Read(buffer, 0, 120);
                            while (imgData.Count < imgSize)
                            {
                                imgReader.BaseStream.Read(buffer, 0, 1);
                                imgData.Add(buffer[0]);
                            }
                        }

                        MemoryStream stream = new MemoryStream(imgData.ToArray());
                        //BinaryReader reader = new BinaryReader(stream);
                        bmpImage = (Bitmap)Image.FromStream(stream);

                        memstream.Close();
                        stream.Close();

                        //Thread.Sleep(1);
                        if (CanSend)
                        {
                            StatSender("LIVEVIEW");
                        }
                    }
                }

            }
            catch (Exception ex)
            {
                throw ex;
                //MessageBox.Show(ex.Message);
            }
            //e.Cancel = true;
        }

        private void TakePicture_DoWork(object sender, DoWorkEventArgs e)
        {
            BackgroundWorker worker = sender as BackgroundWorker;
            WebClient imageClient = new WebClient();                                                // Initializes webclient for image managing
            onSave = true;                                                                          // Sets OnSave flag
            CamResponse = SendRequest("actTakePicture", "");                                        // Sends HTTP GET request, retrieves image URL
            string imgURL = ReadRequestJson(CamResponse, 0, 0);                                            //NON JSON SOLUTION: CamResponse.Substring(20).Split('\"').FirstOrDefault();
            StatSender("PICTURE");
            //if (SaveQuery)
                imageClient.DownloadFile(imgURL, SavePath + "\\" + SaveName);                                   // Saves File
        }

        private void TakePicture_RunWorkerCompleted1(object sender, RunWorkerCompletedEventArgs e)
        {
            StatSender("IMGSAVED");
        }

        private static void ThreadProc()                                                        // Connection timer, manages timeout OnConnection to cammera
        {
            Thread.Sleep(10000);
        }
    }


}
