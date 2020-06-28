namespace astron.distributed
{
    public class DistributedObjectGlobalUD : DistributedObjectUD
    {
        public DistributedObjectGlobalUD()
        {
            doNotDeallocateChannel = true;
        }

        public override void AnnounceGenerate()
        {
            air.RegisterForChannel(doId);
            base.AnnounceGenerate();
        }

        public override void Delete()
        {
            air.UnregisterForChannel(doId);
            base.Delete();
        }
    }
}
