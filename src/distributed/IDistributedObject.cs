using astron.util;

namespace astron.distributed
{
    public interface IDistributedObject
    {
        DCClass dclass
        {
            get;
            set;
        }

        uint doId
        {
            get;
            set;
        }

        uint parentId
        {
            get;
            set;
        }

        uint zoneId
        {
            get;
            set;
        }

        bool doNotDeallocateChannel
        {
            get;
            set;
        }

        bool neverDisable
        {
            get;
            set;
        }

        void SendDeleteEvent();
        void Delete();
        void GenerateInit();
        void Generate();
        void AnnounceGenerate();
        void SetLocation(uint parentId, uint zoneId);
        void UpdateRequiredFields(DCClass dclass, DatagramIterator di);
        void UpdateAllRequiredFields(DCClass dclass, DatagramIterator di);
        void UpdateAllRequiredOtherFields(DCClass dclass, DatagramIterator di);
        void HandleChildArrive(IDistributedObject distObj, uint zoneId);
        void HandleChildArriveZone(IDistributedObject distObj, uint zoneId);
        void HandleChildLeave(IDistributedObject distObj, uint oldZoneId);
        void HandleChildLeaveZone(IDistributedObject distObj, uint oldZoneId);
        void PostGenerateMessage();
        void SendGenerateWithRequired(uint parentId, uint zoneId);
    }
}
