namespace astron.distributed
{
    /// <summary>
    /// The Distributed Object Global class is the base class for global
    /// network based (i.e. distributed) objects.
    /// </summary>
    public class DistributedObjectGlobal : DistributedObject
    {
        public DistributedObjectGlobal()
        {
            parentId = 0;
            zoneId = 0;
        }
    }
}
