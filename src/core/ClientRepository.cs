using System;

using astron.distributed;
using astron.util;

#if UNITY_5_3_OR_NEWER
using UnityEngine;
#endif

namespace astron.core
{
    public class ClientRepository : ConnectionRepository
    {
        public event Action onHelloEvent;

        private static ClientRepository instance;

        public static ClientRepository Instance()
        {
            return instance;
        }

        public ClientRepository()
        {
            if (instance != null)
            {
                throw new Exception("Attempted to instantiate multiple client repositories");
            }

            instance = this;
        }

        public void SendHello(string version)
        {
            Datagram dg = new Datagram(MsgTypes.CLIENT_HELLO);
            dg.WriteUint32(0xDEADBEEF); // TODO: Fix dc hash mismatch issue.
            dg.WriteString(version);
            Send(dg);
        }

        protected override void HandleDatagram(DatagramIterator di)
        {
            ushort msgType = di.ReadUint16();

            switch ((MsgTypes)msgType)
            {
                case MsgTypes.CLIENT_HELLO_RESP:
                    HandleHelloResp();
                    break;
                case MsgTypes.CLIENT_EJECT:
                    HandleEject(di);
                    break;
                case MsgTypes.CLIENT_ENTER_OBJECT_REQUIRED:
                    HandleGenerateWithRequired(di);
                    break;
                case MsgTypes.CLIENT_ENTER_OBJECT_REQUIRED_OTHER:
                    break;
                case MsgTypes.CLIENT_ENTER_OBJECT_REQUIRED_OTHER_OWNER:
                    break;
                case MsgTypes.CLIENT_OBJECT_SET_FIELD:
                    HandleUpdateField(di);
                    break;
                case MsgTypes.CLIENT_OBJECT_LEAVING:
                    break;
                case MsgTypes.CLIENT_OBJECT_LEAVING_OWNER:
                    break;
                case MsgTypes.CLIENT_DONE_INTEREST_RESP:
                    break;
                case MsgTypes.CLIENT_OBJECT_LOCATION:
                    break;
                default:
                    Log($"Unknown message type: {msgType}");
                    break;
            }
        }

        private void HandleHelloResp()
        {
            onHelloEvent?.Invoke();
        }

        private void HandleEject(DatagramIterator di)
        {
            ushort errorCode = di.ReadUint16();
            string reason = di.ReadString();

            Log($"Disconnected from remote server {errorCode} - {reason}");
        }

        private IDistributedObject GenerateWithRequiredFields(DCClass dclass, uint doId, DatagramIterator di, uint parentId, uint zoneId)
        {
            IDistributedObject distObj;

            if (doId2do.TryGetValue(doId, out distObj))
            {
                // ...it is in our dictionary.
                // Just update it.
                System.Diagnostics.Debug.Assert(distObj.dclass == dclass);
                distObj.Generate();
                distObj.SetLocation(parentId, zoneId);
                distObj.UpdateRequiredFields(dclass, di);
                // UpdateRequiredFields calls AnnounceGenerate.
            }
            else
            {
                // ...it is not in the dictionary or the cache.
                // Construct a new one.

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
                    return null;
                }

                distObj.dclass = dclass;
                // Assign it an Id.
                distObj.doId = doId;
                // Put the new do in the dictionary.
                doId2do.Add(doId, distObj);
                // Update the required fields
                distObj.GenerateInit(); // Only called when constructed.
                distObj.Generate();
                distObj.SetLocation(parentId, zoneId);
                distObj.UpdateRequiredFields(dclass, di);
                // UpdateRequiredFields calls AnnounceGenerate.
            }

            return distObj;
        }

        private void HandleGenerateWithRequired(DatagramIterator di)
        {
            uint doId = di.ReadUint32();
            uint parentId = di.ReadUint32();
            uint zoneId = di.ReadUint32();
            ushort classId = di.ReadUint16();

            DCClass dclass = dclassesByNumber[classId];

            dclass.start_generate();
            GenerateWithRequiredFields(dclass, doId, di, parentId, zoneId);
            dclass.stop_generate();
        }
    }
}
