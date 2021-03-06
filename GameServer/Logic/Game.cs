﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using LeagueSandbox.GameServer.Logic;
using LeagueSandbox.GameServer.Core.Logic.PacketHandlers;
using ENet;
using LeagueSandbox.GameServer.Logic.Packets;
using LeagueSandbox.GameServer.Logic.Maps;
using BlowFishCS;
using System.Threading;
using LeagueSandbox.GameServer.Logic.API;
using LeagueSandbox.GameServer.Logic.Content;
using LeagueSandbox.GameServer.Logic.Chatbox;
using LeagueSandbox.GameServer.Logic.Chatbox.Commands;
using LeagueSandbox.GameServer.Logic.Players;

namespace LeagueSandbox.GameServer.Core.Logic
{
    public class Game
    {
        private Host _server;
        public BlowFish Blowfish { get; private set; }

        public bool IsRunning { get; private set; }
        public int PlayersReady { get; private set; }

        public Map Map { get; private set; }
        public PacketNotifier PacketNotifier { get; private set; }
        public PacketHandlerManager PacketHandlerManager { get; private set; }
        public Config Config { get; protected set; }
        protected const int PEER_MTU = 996;
        protected const PacketFlags RELIABLE = PacketFlags.Reliable;
        protected const PacketFlags UNRELIABLE = PacketFlags.None;
        protected const double REFRESH_RATE = 16.666; // 60 fps
        private long _timeElapsed;
        private Logger _logger;
        // Object managers
        private ItemManager _itemManager;
        // Other managers
        private ChatCommandManager _chatCommandManager;
        private PlayerManager _playerManager;
        private NetworkIdManager _networkIdManager;

        public Game(
            ItemManager itemManager,
            ChatCommandManager chatCommandManager,
            NetworkIdManager networkIdManager,
            PlayerManager playerManager,
            Logger logger
        ) {
            _itemManager = itemManager;
            _chatCommandManager = chatCommandManager;
            _networkIdManager = networkIdManager;
            _playerManager = playerManager;
            _logger = logger;
        }

        public bool Initialize(Address address, string baseKey)
        {
            _logger.LogCoreInfo("Loading Config.");

            var programsConfigPath = Program.GameInfoPath;
            var configPath = "Settings/GameInfo.json";
            if (!string.IsNullOrEmpty(programsConfigPath))
            {
                configPath = Program.GameInfoPath;
            }

            Config = new Config(configPath);

            _chatCommandManager.LoadCommands();

            _server = new Host();
            _server.Create(address, 32, 32, 0, 0);

            var key = Convert.FromBase64String(baseKey);
            if (key.Length <= 0)
            {
                return false;
            }

            Blowfish = new BlowFish(key);
            PacketHandlerManager = new PacketHandlerManager(_logger, Blowfish, _server, _playerManager);

            RegisterMap((byte)Config.GameConfig.Map);

            PacketNotifier = new PacketNotifier(this, _playerManager, _networkIdManager);
            ApiFunctionManager.SetGame(this);
            IsRunning = false;

            foreach (var p in Config.Players)
            {
                _playerManager.AddPlayer(p);
            }

            return true;
        }

        public void RegisterMap(byte mapId)
        {
            var mapName = Config.ContentManager.GameModeName + "-Map" + mapId;
            var dic = new Dictionary<string, Type>
            {
                { "LeagueSandbox-Default-Map1", typeof(SummonersRift) },
                // { "LeagueSandbox-Default-Map8", typeof(CrystalScar) },
                { "LeagueSandbox-Default-Map10", typeof(TwistedTreeline) },
                // { "LeagueSandbox-Default-Map11", typeof(NewSummonersRift) },
                { "LeagueSandbox-Default-Map12", typeof(HowlingAbyss) },
            };

            if (!dic.ContainsKey(mapName))
            {
                Map = new SummonersRift(this);
                return;
            }

            Map = (Map)Activator.CreateInstance(dic[mapName], this);
        }

        public void NetLoop()
        {
            var watch = new Stopwatch();
            var enetEvent = new Event();
            while (!Program.IsSetToExit)
            {
                while (_server.Service(0, out enetEvent) > 0)
                {
                    switch (enetEvent.Type)
                    {
                        case EventType.Connect:
                            //Logging->writeLine("A new client connected: %i.%i.%i.%i:%i", event.peer->address.host & 0xFF, (event.peer->address.host >> 8) & 0xFF, (event.peer->address.host >> 16) & 0xFF, (event.peer->address.host >> 24) & 0xFF, event.peer->address.port);

                            // Set some defaults
                            enetEvent.Peer.Mtu = PEER_MTU;
                            enetEvent.Data = 0;
                            break;

                        case EventType.Receive:
                            if (!PacketHandlerManager.handlePacket(enetEvent.Peer, enetEvent.Packet, (Channel)enetEvent.ChannelID))
                            {
                                //enet_peer_disconnect(event.peer, 0);
                            }

                            // Clean up the packet now that we're done using it.
                            enetEvent.Packet.Dispose();
                            break;

                        case EventType.Disconnect:
                            HandleDisconnect(enetEvent.Peer);
                            break;
                    }
                }
                if (IsRunning)
                {
                    Map.Update(_timeElapsed);
                }
                watch.Stop();
                _timeElapsed = watch.ElapsedMilliseconds;
                watch.Restart();
                var timeToWait = REFRESH_RATE - _timeElapsed;
                if (timeToWait < 0)
                {
                    timeToWait = 0;
                }
                Thread.Sleep((int)timeToWait);
            }
        }

        public void IncrementReadyPlayers()
        {
            PlayersReady++;
        }

        public void Start()
        {
            IsRunning = true;
        }

        public void Stop()
        {
            IsRunning = false;
        }

        private bool HandleDisconnect(Peer peer)
        {
            var peerinfo = _playerManager.GetPeerInfo(peer);
            if (peerinfo != null)
            {
                // TODO: Handle disconnect
                _logger.LogCoreInfo("Player " + peerinfo.UserId + " disconnected");
            }
            return true;
        }
    }
}
