using astron.core;
using astron.util;

namespace astron.distributed
{
    public enum ActiveState
    {
        ESNew = 1,
        ESDeleted,
        ESDisabling,
        ESDisabled, // Values here and lower are considered "disabled".
        ESGenerating, // Values here and greater are considered "generated".
        ESGenerated
    }

    public class DistributedObject : IDistributedObject
    {
        public DCClass dclass { get; set; }
        public uint doId { get; set; }
        public uint parentId { get; set; }
        public uint zoneId { get; set; }
        public bool doNotDeallocateChannel { get; set; }
        public bool neverDisable { get; set; }

        protected ActiveState activeState;

        private ClientRepository cr;

        public DistributedObject()
        {
            cr = ClientRepository.Instance();
        }

        /// <summary>
        /// This is called just before we get deleted.
        /// </summary>
        public void SendDeleteEvent()
        {
        }

        /// <summary>
        /// Inheritors should redefine this to take appropriate action on delete.
        /// </summary>
        public void Delete()
        {
        }

        /// <summary>
        /// This method is called when the DistributedObject is first introduced
        /// to the world... Not when it is pulled from the cache.
        /// </summary>
        public void GenerateInit()
        {
            activeState = ActiveState.ESGenerating;
        }

        /// <summary>
        /// Inheritors should redefine this to take appropriate action on generate.
        /// </summary>
        public void Generate()
        {
            activeState = ActiveState.ESGenerating;
        }

        /// <summary>
        /// Sends a message to the world after the object has been
        /// generated and all of its required fields filled in.
        /// </summary>
        public void AnnounceGenerate()
        {
        
        }

        public void SetLocation(uint parentId, uint zoneId)
        { 
            // TODO.
        }

        public void SendUpdate(string fieldName, uint sendToId, params object[] args)
        {
            Datagram dg = cr.ClientFormatUpdate(dclass, fieldName, sendToId, args);
            cr.Send(dg);
        }

        public void SendUpdate(string fieldName, params object[] args)
        {
            SendUpdate(fieldName, doId, args);
        }

        public void UpdateRequiredFields(DCClass dclass, DatagramIterator di)
        {
            // TODO.   
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

        public void UpdateAllRequiredFields(DCClass dclass, DatagramIterator di)
        {
        }

        public void UpdateAllRequiredOtherFields(DCClass dclass, DatagramIterator di)
        {
        }

        public void PostGenerateMessage()
        {
            if (activeState != ActiveState.ESGenerated)
            {
                activeState = ActiveState.ESGenerated;
            }
        }

        public void SendGenerateWithRequired(uint parentId, uint zoneId)
        {
        }
    }
}
