using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation; // per IPEndPoint, IPAddress, Dns
using System.Net.Sockets;       // udb socket
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace csGameIorga;

public class Comunicator
{

    internal IPEndPoint localEndPoint;
    internal IPEndPoint remoteEndPoint;
    private string nick;
    internal string Nickname
    {
        set { nick = value; _formHook.UpdateNickname(value); LogMessage($"Set nickname to {value}"); }
        get { return nick; }
    }
    private readonly Messenger _formHook;
    private CancellationTokenSource _listenerCts;
    private CancellationTokenSource _senderCts;
    private Task? _listenerTask;
    private Task? _sendTask;

    // private readonly int _portSwitchMaxAttempts;
    // private int _portSwitchAttempts;

    public Comunicator(
        Messenger formHook, 
        string nick="Guest",
        int localPort = 5000, int remotePort = 5000,
        string listenIp = "0.0.0.0", string remoteIp = "127.0.0.1"
        //,int portSwitchMaxAttempts = 15
        )
    {
        // _portSwitchAttempts = 0;
        // _portSwitchMaxAttempts = portSwitchMaxAttempts;
        _formHook = formHook;
        Nickname = nick;
        
        UpdateLocalEndPoint(new IPEndPoint(IPAddress.Parse(listenIp), localPort));
        UpdateRemoteEndPoint(new IPEndPoint(IPAddress.Parse(remoteIp), remotePort));
    }

    private void LogMessage(string message)
    {
        // In order to access proprieties of the form thread the caller needs to be on the same thread
        Application.Current.Dispatcher.Invoke(() => _formHook.Logger(message));
    }

    private void LogMessage(string message, IPEndPoint sender)
    {
        // In order to access proprieties of the form thread the caller needs to be on the same thread
        Application.Current.Dispatcher.Invoke(() => _formHook.Logger($"{sender} sent: {message}"));
    }

    private void _HandleIncoming(UdpReceiveResult data)
    {
        var buffer = Encoding.ASCII.GetString(data.Buffer);
        var sender = data.RemoteEndPoint;
        try
        {
            var pattern = new Regex(@"(?<name>\w+);(?<command>\w+);(?<data>.+)", RegexOptions.IgnoreCase);
            var matches = pattern.Matches(buffer);
            if (matches.Count <= 0) 
                throw new InvalidDataException("Provided data could not be parsed!");
            GroupCollection groups = matches[0].Groups;
            var nickname = groups["name"].Value;
            var command = groups["command"].Value;
            var rawData = groups["data"].Value;
            // attemps to generate a specialized ICommand using the specific generator based on the command
            if (Messenger.commandsMap.TryGetValue(command, out var generator))
                _formHook.Execute(generator(nickname, command, rawData), sender);
            else
                throw new ();
        }
        catch (Exception)
        {
            LogMessage(buffer, sender);
        }

    }

    private async Task _Send(string message)
    {
        //var sendBuffer = Encoding.ASCII.GetBytes(message);
        //var sendClient = new UdpClient();
        //var sentBytes = await sendClient.SendAsync(sendBuffer, remoteEndPoint, _senderCts.Token);
        //sendClient.Close();
        var sentBytes = await Comunicator.Send(message, remoteEndPoint);
        this.LogMessage($"Sent {sentBytes} bytes to {remoteEndPoint}!\n");
    }
    public static async Task<int> Send(string message, IPEndPoint remoteEndPoint)
    {
        var sendBuffer = Encoding.ASCII.GetBytes(message);
        var sendClient = new UdpClient();
        var sentBytes = await sendClient.SendAsync(sendBuffer, remoteEndPoint);
        sendClient.Close();
        return sentBytes;
    }

    private async Task _Listen(IPEndPoint newEndPoint)
    {
        if (IPGlobalProperties.GetIPGlobalProperties().GetActiveUdpListeners()
            .Any(p => p.Port == newEndPoint.Port))
        {
            LogMessage($"{newEndPoint} is busy, try another port\n");
            return;
        }

        var udpListenEngine = new UdpClient(newEndPoint);
        // _portSwitchAttempts = 0; // reset the counter when usable port is found
        localEndPoint = newEndPoint; // update local endpoint after success
        _formHook.UpdateLocalEndPoint(newEndPoint);
        LogMessage($"Now listening at {newEndPoint}\n");
        try
        {
            while (true)
            {
                var receivedResults = await udpListenEngine.ReceiveAsync(_listenerCts.Token);
                _HandleIncoming(receivedResults);
            }
        }
        catch (OperationCanceledException)
        {
            LogMessage($"Stopped Listening at {newEndPoint}\n");
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message);
        }
        finally
        {
            udpListenEngine.Close();
        }
    }

    public void Send(string message)
    {
        if (string.IsNullOrEmpty(message))
        {
            return;
        }

        _sendTask = this._Send(message);
    }

    public void Listen(IPEndPoint newEndPoint)
    {
        _listenerTask = _Listen(newEndPoint);
        // Trace.WriteLine($"isfaulted status is {_listenTask.IsFaulted}");
        // do
        // {
        //     Trace.WriteLine($"{_portSwitchAttempts} attempts made!");
        //     if (_portSwitchAttempts >= _portSwitchMaxAttempts)
        //     {
        //         LogMessage("Could not find a port to listen on! Stopped listening.");
        //         return;
        //     }
        //     Trace.WriteLine($"Endpoint {_localEndPoint.ToString()} is busy.");
        //     // this cannot change the input value in the wpf app
        //     this.UpdateLocalEndPoint(_localEndPoint.Address, _localEndPoint.Port + 1);
        //     Trace.WriteLine($"Trying on {_localEndPoint}");
        //     _portSwitchAttempts += 1;
        // } while (_listenTask.IsFaulted );
    }

    public void UpdateLocalEndPoint(IPAddress newIp, int newPort)
    {
        this.UpdateLocalEndPoint(new IPEndPoint(newIp, newPort));
    }

    public void UpdateLocalEndPoint(IPEndPoint newEndPoint)
    {
        if (newEndPoint.Equals(localEndPoint))
            return;
        if (_listenerTask != null)
            _listenerCts.Cancel();

        _listenerCts?.Dispose();
        _listenerCts = new CancellationTokenSource();
        Listen(newEndPoint);
    }

    public void UpdateRemoteEndPoint(IPAddress newIp, int newPort)
    {
        UpdateRemoteEndPoint(new IPEndPoint(newIp, newPort));
    }

    public void UpdateRemoteEndPoint(IPEndPoint newEndPoint)
    {
        if (newEndPoint.Equals(remoteEndPoint))
            return;
        _sendTask?.Wait(); // wait for send to finish
        _senderCts?.Dispose();
        _senderCts = new CancellationTokenSource();
        remoteEndPoint = newEndPoint;
        _formHook.UpdateRemoteEndPoint(newEndPoint);
        LogMessage($"Remote EndPoint set to {remoteEndPoint.Address}:{remoteEndPoint.Port}\n");
    }

    public void Shutdown()
    {
        _sendTask?.Wait();
        if (_listenerTask != null)
            _listenerCts.Cancel();
    }
}