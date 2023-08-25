﻿using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using UniRx;
using UnityEngine;

public class ConnectedClientHandler : IConnectedClientHandler
{
    private readonly Subject<JsonSerializable> receivedMessageStream = new();
    public IObservable<JsonSerializable> ReceivedMessageStream => receivedMessageStream;

    public IPEndPoint ClientIpEndPoint { get; private set; }
    public string ClientName { get; private set; }
    public string ClientId { get; private set; }
    public TcpListener ClientTcpListener { get; private set; }

    private readonly IServerSideConnectRequestManager serverSideConnectRequestManager;

    private readonly object streamReaderLock = new();
    private readonly Thread receiveDataThread;
    private readonly Thread clientStillAliveCheckThread;

    private TcpClient tcpClient;
    private NetworkStream tcpClientStream;
    private StreamReader tcpClientStreamReader;
    private StreamWriter tcpClientStreamWriter;

    private bool isDisposed;

    public ConnectedClientHandler(
        IServerSideConnectRequestManager serverSideConnectRequestManager,
        IPEndPoint clientIpEndPoint,
        string clientName,
        string clientId)
    {
        this.serverSideConnectRequestManager = serverSideConnectRequestManager;
        ClientIpEndPoint = clientIpEndPoint;
        ClientName = clientName;
        ClientId = clientId;
        if (ClientId.IsNullOrEmpty())
        {
            throw new ArgumentException("Attempt to create ConnectedClientHandler without ClientId");
        }

        ClientTcpListener = new TcpListener(IPAddress.Any, 0);
        ClientTcpListener.Start();
        
        Debug.Log($"Started TcpListener on port {ClientTcpListener.GetPort()} to receive messages from Companion App");
        receiveDataThread = new Thread(() =>
        {
            while (!isDisposed)
            {
                if (tcpClient == null)
                {
                    try
                    {
                        tcpClient = ClientTcpListener.AcceptTcpClient();
                        tcpClient.NoDelay = true;
                        tcpClientStream = tcpClient.GetStream();
                        tcpClientStreamReader = new StreamReader(tcpClientStream);
                        tcpClientStreamWriter = new StreamWriter(tcpClientStream);
                        tcpClientStreamWriter.AutoFlush = true;
                    }
                    catch (Exception e)
                    {
                        Debug.LogError("Error when accepting TcpClient for Companion App. Closing TcpListener.");
                        Debug.LogException(e);
                        this.serverSideConnectRequestManager.RemoveConnectedClientHandler(this);
                        return;
                    }
                }
                else
                {
                    try
                    {
                        ReadMessagesFromClient();
                    }
                    catch (Exception e)
                    {
                        Debug.LogError("Error when reading TcpClient message. Closing TcpListener.");
                        Debug.LogException(e);
                        this.serverSideConnectRequestManager.RemoveConnectedClientHandler(this);
                        return;
                    }
                }

                Thread.Sleep(250);
            }
        });
        receiveDataThread.Start();
        
        clientStillAliveCheckThread = new Thread(() =>
        {
            while (!isDisposed)
            {
                if (tcpClient != null
                    && tcpClientStream != null)
                {
                    CheckClientStillAlive();
                }
                Thread.Sleep(1500);
            }
        });
        clientStillAliveCheckThread.Start();
    }
    
    private void CheckClientStillAlive()
    {
        try
        {
            // If there is new data available, then the client is still alive.
            if (!tcpClientStream.DataAvailable)
            {
                // Try to send something to the client.
                // If this fails with an Exception, then the connection has been lost and the client has to reconnect.
                tcpClientStreamWriter.WriteLine(new StillAliveCheckDto().ToJson());
                tcpClientStreamWriter.Flush();
            }
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            Debug.LogError("Failed sending data to client. Removing ConnectedClientHandler.");
            serverSideConnectRequestManager.RemoveConnectedClientHandler(this);
        }
    }

    public void ReadMessagesFromClient()
    {
        lock (streamReaderLock)
        {
            while (tcpClientStream != null
                   && tcpClientStream.DataAvailable)
            {
                ReadMessageFromClient();
            }
        }
    }

    private void ReadMessageFromClient()
    {
        string line = tcpClientStreamReader.ReadLine();
        if (line.IsNullOrEmpty())
        {
            return;
        }

        line = line.Trim();
        if (!line.StartsWith("{")
            || !line.EndsWith("}"))
        {
            Debug.LogWarning("Received invalid JSON from client.");
            return;
        }

        HandleJsonMessageFromClient(line);
    }

    public void SendMessageToClient(JsonSerializable jsonSerializable)
    {
        if (tcpClient != null
            && tcpClientStream != null
            && tcpClientStreamWriter != null
            && tcpClientStream.CanWrite)
        {
            tcpClientStreamWriter.WriteLine(jsonSerializable.ToJson());
            tcpClientStreamWriter.Flush();
        }
        else
        {
            Debug.LogWarning("Cannot send message to client.");
        }
    }

    private void HandleJsonMessageFromClient(string json)
    {
        if (!CompanionAppMessageUtils.TryGetMessageType(json, out CompanionAppMessageType messageType))
        {
            return;
        }
        switch (messageType)
        {
            case CompanionAppMessageType.StillAliveCheck:
                // Nothing to do. If the connection would not be still alive anymore, then this message would have failed already.
                return;
            case CompanionAppMessageType.BeatPitchEvents:
                BeatPitchEventsDto beatPitchEventsDto = JsonConverter.FromJson<BeatPitchEventsDto>(json);
                beatPitchEventsDto.BeatPitchEvents
                    .ForEach(beatPitchEventDto => receivedMessageStream.OnNext(beatPitchEventDto));
                return;
            default:
                Debug.Log($"Unknown MessageType {messageType} in JSON from server: {json}");
                return;
        }
    }

    public void Dispose()
    {
        isDisposed = true;
        tcpClientStream?.Close();
        tcpClient?.Close();
        ClientTcpListener?.Stop();
    }
}
