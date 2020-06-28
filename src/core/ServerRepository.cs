using System;
using System.Collections.Generic;

using astron.distributed;
using astron.util;

#if UNITY_5_3_OR_NEWER
using UnityEngine;
#endif

namespace astron.core
{
    public class ServerRepository : ConnectionRepository
    {
        public ulong ourChannel;
        public ulong stateServer;

        protected UniqueIdAllocator channelAllocator;
        protected List<ulong> registeredChannels = new List<ulong>();

        private static ServerRepository instance;

        public static ServerRepository Instance()
        {
            return instance;
        }

        public ServerRepository(uint baseChannel, ulong serverId = 0, string suffix = "AI", string[] dcFileNames = null)
        {
            if (instance != null)
            {
                throw new Exception("Attempted to instantiate multiple server repositories");
            }

            dcSuffix = suffix;

            clientDatagram = false;

            // The state server we are configured to use for creating objects.
            // If this is 0, generating objects is not possible.
            stateServer = serverId;

            uint maxChannels = 1000000; // TODO.
            channelAllocator = new UniqueIdAllocator(baseChannel, baseChannel + maxChannels - 1);

            // Allocate a channel for ourself.
            ourChannel = AllocateChannel();

            ReadDCFile(dcFileNames);

            onConnectEvent += new Action(HandleConnected);

            instance = this;
        }

        /// <summary>
        /// Allocate an unused channel out of this AIR's configured channel space.
        /// This is also used to allocate IDs for DistributedObjects, since those
        /// occupy a channel.
        /// </summary>
        /// <returns></returns>
        public uint AllocateChannel()
        {
            return channelAllocator.Allocate();
        }

        /// <summary>
        /// Return the previously-allocated channel back to the allocation pool.
        /// </summary>
        /// <param name="channel"></param>
        public void DeallocateChannel(uint channel)
        {
            channelAllocator.Free(channel);
        }

        /// <summary>
        /// Generate an object onto the State Server, choosing an ID from the pool.
        /// You should use do.generateWithRequired(...) instead.This is not meant
        /// to be called directly unless you really know what you are doing.
        /// </summary>
        /// <param name="distObj"></param>
        /// <param name="parentId"></param>
        /// <param name="zoneId"></param>
        public void GenerateWithRequired(IDistributedObject distObj, uint parentId, uint zoneId)
        {
            uint doId = AllocateChannel();
            GenerateWithRequiredAndId(distObj, doId, parentId, zoneId);
        }

        /// <summary>
        /// Generate an object onto the State Server, specifying its ID and location.
        /// You should use do.generateWithRequiredAndId(...) instead. This is not
        /// meant to be called directly unless you really know what you are doing.
        /// </summary>
        /// <param name="distObj"></param>
        /// <param name="doId"></param>
        /// <param name="parentId"></param>
        /// <param name="zoneId"></param>
        public void GenerateWithRequiredAndId(IDistributedObject distObj, uint doId, uint parentId, uint zoneId)
        {
            distObj.doId = doId;
            AddDOToTables(distObj, parentId, zoneId);
            distObj.SendGenerateWithRequired(parentId, zoneId);
        }

        /// <summary>
        /// Send a field update for the given object.
        /// You should use do.sendUpdate(...) instead. This is not meant to be
        /// called directly unless you really know what you are doing.
        /// </summary>
        /// <param name="distObj"></param>
        /// <param name="fieldName"></param>
        /// <param name="args"></param>
        public void SendUpdate(IDistributedObject distObj, string fieldName, params object[] args)
        {
            SendUpdateToChannel(distObj, distObj.doId, fieldName, args);
        }

        /// <summary>
        /// Send an object field update to a specific channel.
        /// This is useful for directing the update to a specific client or node,
        /// rather than at the State Server managing the object.
        /// You should use do.sendUpdateToChannel(...) instead. This is not meant
        /// to be called directly unless you really know what you are doing.
        /// </summary>
        /// <param name="distObj"></param>
        /// <param name="channelId"></param>
        /// <param name="fieldName"></param>
        /// <param name="args"></param>
        public void SendUpdateToChannel(IDistributedObject distObj, ulong channelId, string fieldName, params object[] args)
        {
            DCField field = distObj.dclass.get_field_by_name(fieldName);
            Datagram dg = AiFormatUpdate(field, distObj.doId, channelId, ourChannel, args);
            Send(dg);
        }

        /// <summary>
        /// Register for messages on a specific Message Director channel.
        /// If the channel is already open by this AIR, nothing will happen.
        /// </summary>
        /// <param name="channel"></param>
        public void RegisterForChannel(ulong channel)
        {
            if (registeredChannels.Contains(channel))
            {
                return;
            }

            registeredChannels.Add(channel);

            Datagram dg = new Datagram();
            dg.WriteServerControlHeader(MsgTypes.CONTROL_ADD_CHANNEL);
            dg.WriteChannel(channel);
            Send(dg);
        }

        /// <summary>
        /// Unregister a channel subscription on the Message Director. The Message
        /// Director will cease to relay messages to this AIR sent on the channel.
        /// </summary>
        /// <param name="channel"></param>
        public void UnregisterForChannel(ulong channel)
        {
            if (!registeredChannels.Contains(channel))
            {
                return;
            }

            registeredChannels.Remove(channel);

            Datagram dg = new Datagram();
            dg.WriteServerControlHeader(MsgTypes.CONTROL_REMOVE_CHANNEL);
            dg.WriteChannel(channel);
            Send(dg);
        }

        public void AddPostRemove(Datagram removeDatagram)
        {
            Datagram dg = new Datagram();
            dg.WriteServerControlHeader(MsgTypes.CONTROL_ADD_POST_REMOVE);
            dg.WriteChannel(ourChannel);
            dg.WriteString(removeDatagram.GetMessage());
            Send(dg);
        }

        /// <summary>
        /// Eject a client from the client agent using the client's channel.
        /// </summary>
        /// <param name="connId"></param>
        /// <param name="reason"></param>
        /// <param name="code"></param>
        public void KillConnection(ulong connId, string reason, ushort code = 122)
        {
            Datagram dg = new Datagram();
            dg.WriteServerHeader(connId, ourChannel, MsgTypes.CLIENTAGENT_EJECT);
            dg.WriteUint16(code);
            dg.WriteString(reason);
            Send(dg);
        }

        /// <summary>
        /// Set the connection name as identified by the message director.
        /// Useful for debugging purposes.
        /// </summary>
        /// <param name="name"></param>
        public void SetConName(string name)
        {
            Datagram dg = new Datagram();
            dg.WriteServerControlHeader(MsgTypes.CONTROL_SET_CON_NAME);
            dg.WriteString(name);
            Send(dg);
        }

        protected override void HandleDatagram(DatagramIterator di)
        {
            ushort msgType = di.ReadUint16();

            switch ((MsgTypes)msgType)
            {
                case MsgTypes.STATESERVER_OBJECT_SET_FIELD:
                    HandleUpdateField(di);
                    break;
                case MsgTypes.STATESERVER_OBJECT_ENTER_AI_WITH_REQUIRED:
                case MsgTypes.STATESERVER_OBJECT_ENTER_AI_WITH_REQUIRED_OTHER:
                    HandleObjEntry(di, msgType == (ushort)MsgTypes.STATESERVER_OBJECT_ENTER_AI_WITH_REQUIRED_OTHER);
                    break;
                case MsgTypes.STATESERVER_OBJECT_CHANGING_AI:
                case MsgTypes.STATESERVER_OBJECT_DELETE_RAM:
                    HandleObjExit(di);
                    break;
                case MsgTypes.STATESERVER_OBJECT_CHANGING_LOCATION:
                    HandleObjLocation(di);
                    break;
                case MsgTypes.DBSERVER_CREATE_OBJECT_RESP:
                case MsgTypes.DBSERVER_OBJECT_GET_ALL_RESP:
                case MsgTypes.DBSERVER_OBJECT_GET_FIELDS_RESP:
                case MsgTypes.DBSERVER_OBJECT_GET_FIELD_RESP:
                case MsgTypes.DBSERVER_OBJECT_SET_FIELD_IF_EQUALS_RESP:
                case MsgTypes.DBSERVER_OBJECT_SET_FIELDS_IF_EQUALS_RESP:
                    break;
                case MsgTypes.DBSS_OBJECT_GET_ACTIVATED_RESP:
                    break;
                case MsgTypes.STATESERVER_OBJECT_GET_LOCATION_RESP:
                    HandleGetLocationResp(di);
                    break;
                case MsgTypes.STATESERVER_OBJECT_GET_ALL_RESP:
                    HandleGetObjectResp(di);
                    break;
                case MsgTypes.CLIENTAGENT_GET_NETWORK_ADDRESS_RESP:
                    HandleGetNetworkAddressResp(di);
                    break;
                default:
                    Log($"Unknown message type: {msgType}");
                    break;
            }
        }

        private void HandleConnected()
        {
            // Listen to our channel...
            RegisterForChannel(ourChannel);

            // If we're configured with a State Server, register a post-remove to
            // clean up whatever objects we own on this server should we unexpectedly
            // fall over and die.
            if (stateServer != 0)
            {
                Datagram dg = new Datagram();
                dg.WriteServerHeader(stateServer, ourChannel, MsgTypes.STATESERVER_DELETE_AI_OBJECTS);
                dg.WriteChannel(ourChannel);
                AddPostRemove(dg);
            }
        }

        private void HandleObjEntry(DatagramIterator di, bool other)
        {
            uint doId = di.ReadUint32();
            uint parentId = di.ReadUint32();
            uint zoneId = di.ReadUint32();
            ushort classId = di.ReadUint16();

            if (!dclassesByNumber.ContainsKey(classId))
            {
                Log($"Received entry for unknown dclass {classId} (DoId: {doId})");
                return;
            }

            if (doId2do.ContainsKey(doId))
            {
                // We already know about this object; ignore the entry.
                return;
            }

            DCClass dclass = dclassesByNumber[classId];

            IDistributedObject distObj;

            // Try instantiate the dclass type.
            if (dcImportsType.TryGetValue(dclass.get_name(), out Type doType))
            {
                distObj = (IDistributedObject)Activator.CreateInstance(doType);
            }
#if UNITY_5_3_OR_NEWER
            else if (dcImportsPrefab.TryGetValue(dclass.get_name(), out GameObject doPrefab))
            {
                GameObject sceneObj = UnityEngine.Object.Instantiate(doPrefab);
                distObj = sceneObj.GetComponent<IDistributedObject>();
            }
#endif
            else
            {
                Log($"Could not create an undefined {dclass.get_name()} object.");
                return;
            }

            distObj.dclass = dclass;
            distObj.doId = doId;
            // The DO came in off the server, so we do not unregister the channel when
            // it dies:
            distObj.doNotDeallocateChannel = true;
            AddDOToTables(distObj, parentId, zoneId);

            // Now for generation:
            distObj.Generate();
            if (other)
            {
                distObj.UpdateAllRequiredOtherFields(dclass, di);
            }
            else
            {
                distObj.UpdateAllRequiredFields(dclass, di);
            }
        }

        private void HandleObjExit(DatagramIterator di)
        {
            uint doId = di.ReadUint32();

            if (!doId2do.ContainsKey(doId))
            {
                Log($"Received AI exit for unknown object {doId}");
                return;
            }

            IDistributedObject distObj = doId2do[doId];
            distObj.Delete();
            distObj.SendDeleteEvent();
        }

        private void HandleObjLocation(DatagramIterator di)
        {
            uint doId = di.ReadUint32();
            uint parentId = di.ReadUint32();
            uint zoneId = di.ReadUint32();

            IDistributedObject distObj = doId2do[doId];
            if (distObj == null)
            {
                Log($"Received location for unknown doId {doId}");
                return;
            }

            distObj.SetLocation(parentId, zoneId);
        }

        private void HandleGetLocationResp(DatagramIterator di)
        {
            uint ctx = di.ReadUint32();
            uint doId = di.ReadUint32();
            uint parentId = di.ReadUint32();
            uint zoneId = di.ReadUint32();

            throw new NotImplementedException("HandleGetLocationResp");
        }

        private void HandleGetObjectResp(DatagramIterator di)
        {
            uint ctx = di.ReadUint32();
            uint doId = di.ReadUint32();
            uint parentId = di.ReadUint32();
            uint zoneId = di.ReadUint32();
            ushort classId = di.ReadUint16();

            throw new NotImplementedException("HandleGetObjectResp");
        }

        private void HandleGetNetworkAddressResp(DatagramIterator di)
        {
            throw new NotImplementedException("HandleGetNetworkAddressResp");
        }
    }
}
