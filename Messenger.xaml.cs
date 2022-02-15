using System;
using System.Net;
using System.Numerics;
using System.Text.RegularExpressions;
using System.Windows;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace csGameIorga
{
    public abstract class ICommand
    {
        public string Nickname;
        public string CommandStr;
        public string Data;
        protected ICommand(string rawData)
        {
            var pattern = new Regex(@"(?<name>\w+);(?<command>\w+);(?<data>.+)", RegexOptions.IgnoreCase);
            var matches = pattern.Matches(rawData);
            if (matches.Count <= 0)
                throw new InvalidDataException("Provided data could not be parsed!");
            GroupCollection groups = matches[0].Groups;
            this.Nickname = groups["name"].Value;
            this.CommandStr = groups["command"].Value;
            this.Data = groups["data"].Value;
        }
        protected ICommand(string nickname, string command)
        {
            this.Nickname = nickname;
            this.CommandStr = command;
            Data = ""; // should not be used as child has direct access to data
        }
        public abstract string Execute(Board board, Comunicator comunicator, IPEndPoint sender);
    }
    public class Move : ICommand
    {
        private static Regex dataPattern = new Regex(@"(?<x>\d+);(?<y>\d+)", RegexOptions.IgnoreCase);
        public Vector2 Coordinates;
        public Move(string rawData)
            : base(rawData)
        {
            foreach (Match match in dataPattern.Matches(Data)) // isn't null because of the constructor used
            {
                GroupCollection groups = match.Groups;
                this.Coordinates = new Vector2(int.Parse(groups["x"].Value), int.Parse(groups["y"].Value));
            }
        }
        public Move(string nickname, string command, string data)
            : base(nickname, command)
        {
            foreach (Match match in dataPattern.Matches(data))
            {
                GroupCollection groups = match.Groups;
                this.Coordinates = new Vector2(int.Parse(groups["x"].Value), int.Parse(groups["y"].Value));
                Data = this.Coordinates.ToString();
            }
        }
        public override string Execute(Board board, Comunicator comunicator, IPEndPoint sender)
        {
            var status = board.Move(this.Coordinates);
            comunicator.LogMessage($"{(status.Length < 1 ? "Succeeded" : "Failed")} to run <Move> command with vector " +
                   $"{this.Coordinates} sent by {this.Nickname}({sender}){(status.Length < 1 ? "" : " because of <" + status + ">")}\n");
            return status;
        }
    }
    public class Ping : ICommand
    {
        private string message;
        public Ping(string rawData)
            : base(rawData)
        {
            message = Data;
        }
        public Ping(string nickname, string command, string rawData)
            :base(nickname, command)
        {
            message = rawData;
            Data = rawData;
        }
        public override string Execute(Board board, Comunicator comunicator, IPEndPoint sender)
        {
            var status = "";
            comunicator.flipPingFlag();
            if (comunicator.loopDetector(sender, out var t_delta))
            {
                comunicator.LogMessage($"Detected possible loop! Interlocutor might not have a check in place. Disabling ping response!" +
                $"\n    Time delta from last ping is {t_delta}\n");
            };
            if (comunicator.pingFlag())
            {
                comunicator.LogMessage($"Received ping from ({this.Nickname}){sender}! Sending response...\n");
                var ep = new IPEndPoint(sender.Address, comunicator.remoteEndPoint.Port);
                var sendTask = Comunicator.Send($"{comunicator.Nickname};PING;{message}", ep);
                var sentBytes = sendTask.Result;
                comunicator.lowerPingFlag();
                if (sentBytes == 0) status = "No data sent!";
            }
            else
            {
                comunicator.LogMessage($"Received ping response from {sender}\n");
            }
            return status;
        }
    }


    /// <summary>
    /// Interaction logic for Messenger.xaml
    /// </summary>

    public partial class Messenger
    {
        
        public static Dictionary<string, Func<string, string, string, ICommand>> commandsMap = 
            new Dictionary<string, Func<string, string, string, ICommand>>
        {
            ["XY"] = (nickname, command, rawData) => new Move(nickname, command, rawData),
            ["PING"] = (nickname, command, rawData) => new Ping(nickname, command, rawData),
        };
        private readonly Comunicator _comunicator;
        private readonly Board _board;
        public Messenger()
        {
            InitializeComponent();
            _comunicator = new Comunicator(this, Properties.Settings.Default.nickname, Properties.Settings.Default.localPort);
            _board = new Board(this);
            _board.Show();
        }

        private static IPEndPoint GenerateValidEndPoint(string rawIp, string rawPort, string defaultIp="127.0.0.1", int defaultPort=5000)
        {
            IPEndPoint newEp;
            var newIp = IPAddress.Parse(defaultIp);
            var newPort = defaultPort;
            
            try
            {
                newIp = IPAddress.Parse(rawIp);
                newPort = int.Parse(rawPort);
            }
            catch (FormatException) {}
            finally
            {
                newEp = new IPEndPoint(newIp, newPort);
            }
            
            return newEp;
        }
        
        private void SendData(object sender, RoutedEventArgs e)
        {
            _comunicator.Send(m_userInput.Text);
        }

        private void UpdateLocalEndPoint(object sender, RoutedEventArgs e)
        {
            var rawNewIp = _localIp.Text;
            var rawNewPort = _localPort.Text;
            var newEndPoint = GenerateValidEndPoint(rawNewIp, rawNewPort, IPAddress.Loopback.ToString());
            this._comunicator.UpdateLocalEndPoint(newEndPoint);
            // updating the fields with the correct values
            UpdateLocalEndPoint(newEndPoint);
        }        
        
        private void UpdateRemoteEndPoint(object sender, RoutedEventArgs e)
        {
            var rawNewIp = _remoteIp.Text;
            var rawNewPort = _remotePort.Text;
            var newEndPoint = GenerateValidEndPoint(rawNewIp, rawNewPort, IPAddress.Any.ToString());
            this._comunicator.UpdateRemoteEndPoint(newEndPoint);
            
            // updating the fields with the correct values
            UpdateRemoteEndPoint(newEndPoint);
        }

        public void UpdateNickname(string nickname)
        {
            _nickname.Text = nickname;
        }
        private void Nickname_leave(object sender, RoutedEventArgs e)
        {
            _comunicator.Nickname = _nickname.Text;
        }

        public void UpdateLocalEndPoint(IPEndPoint newEndPoint)
        {
            _localIp.Text = newEndPoint.Address.ToString();
            _localPort.Text = newEndPoint.Port.ToString();
        }
        public void UpdateRemoteEndPoint(IPEndPoint newEndPoint)
        {
            _remoteIp.Text = newEndPoint.Address.ToString();
            _remotePort.Text = newEndPoint.Port.ToString();
        }

        private void _saveState()
        {
            Properties.Settings.Default["localPort"] = _comunicator.remoteEndPoint.Port;
            Properties.Settings.Default["nickName"] = _comunicator.Nickname;
            Properties.Settings.Default.Save();
        }

        public void Execute(ICommand command, IPEndPoint sender)
        {
            var status = command.Execute(_board, _comunicator, sender);
        }
        
        public void Logger(string message)
        {
            m_logger.Text += message;
        }

        public void Shutdown()
        {
            _saveState();
            _board.Close();
            _comunicator.Shutdown();
            this.Close();
        }
        
        private void Window_OnClosed(object? sender, EventArgs e)
        {
            this.Shutdown();
        }
    }
}
