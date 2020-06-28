using astron.core;
using astron.util;

namespace astron.distributed
{
    public class DistributedObjectUD : IDistributedObject
    {
        public DCClass dclass { get; set; }
        public uint doId { get; set; }
        public uint parentId { get; set; }
        public uint zoneId { get; set; }
        public bool doNotDeallocateChannel { get; set; }
        public bool neverDisable { get; set; }

        public ServerRepository air;

        public DistributedObjectUD()
        {
            air = ServerRepository.Instance();
            dclass = air.dclassesByName[GetType().Name];
        }

        public ulong GetAccountConnectionChannel(ulong doId)
        {
            return doId + (1003 << 32);
        }

        /// <summary>
        /// Called after the object has been generated and all
        /// of its required fields filled in. Overwrite when needed.
        /// </summary>
        public virtual void AnnounceGenerate()
        {
        }

        /// <summary>
        /// Inheritors should redefine this to take appropriate action on delete.
        /// </summary>
        public virtual void Delete()
        {
            parentId = 0;
            zoneId = 0;
        }

        /// <summary>
        /// Inheritors should put functions that require self.zoneId or
        /// other networked info in this function.
        /// </summary>
        public void Generate()
        {
            air.StoreObjectLocation(this, parentId, zoneId);
        }

        /// <summary>
        /// First generate (not from cache).
        /// </summary>
        public void GenerateInit()
        {
        }

        public void HandleChildArrive(IDistributedObject distObj, uint zoneId)
        {
        }

        public void HandleChildArriveZone(IDistributedObject distObj, uint zoneId)
        {
        }

        public void HandleChildLeave(IDistributedObject distObj, uint oldZoneId)
        {
        }

        public void HandleChildLeaveZone(IDistributedObject distObj, uint oldZoneId)
        {
        }

        /// <summary>
        /// This is called just before we get deleted.
        /// </summary>
        public void SendDeleteEvent()
        {
        }

        public void SetLocation(uint parentId, uint zoneId)
        {
        }

        public void UpdateRequiredFields(DCClass dclass, DatagramIterator di)
        {
        }

        public void UpdateAllRequiredFields(DCClass dclass, DatagramIterator di)
        {
        }

        public void UpdateAllRequiredOtherFields(DCClass dclass, DatagramIterator di)
        {
        }

        public void GenerateWithRequired(uint parentId, uint zoneId)
        {
            // The repository is the one that really does the work.
            this.parentId = parentId;
            this.zoneId = zoneId;
            air.GenerateWithRequired(this, parentId, zoneId);
            Generate();
        }

        public void GenerateWithRequiredAndId(uint doId, uint parentId, uint zoneId)
        {
            // This is a special generate used for estates, or anything else that
            // needs to have a hard coded doId as assigned by the server.

            // The repository is the one that really does the work.
            air.GenerateWithRequiredAndId(this, doId, parentId, zoneId);
            Generate();
            AnnounceGenerate();
            PostGenerateMessage();
        }

        public void SendGenerateWithRequired(uint parentId, uint zoneId)
        {
            Datagram dg = air.AiFormatGenerate(this, doId, parentId, zoneId, air.stateServer, air.ourChannel);
            air.Send(dg);
        }

        public void SendUpdateToChannel(ulong channelId, string fieldName, params object[] args)
        {
            air.SendUpdateToChannel(this, channelId, fieldName, args);
        }

        public void PostGenerateMessage()
        {
        }
    }
}
