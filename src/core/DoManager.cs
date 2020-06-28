using System;
using System.Collections.Generic;

using astron.distributed;

#if UNITY_5_3_OR_NEWER
using UnityEngine;
#endif

namespace astron.core
{
    public class DoManager
    {
        protected Dictionary<uint, IDistributedObject> doId2do = new Dictionary<uint, IDistributedObject>();
        protected Dictionary<uint, IDistributedObject> doId2ownerView = new Dictionary<uint, IDistributedObject>();
        protected Dictionary<uint, Dictionary<uint, List<uint>>> storedDoTable = new Dictionary<uint, Dictionary<uint, List<uint>>>();
        protected List<uint> storedDoIds = new List<uint>();

        public DoManager()
        {

        }

        /// <summary>
        /// Cross platform log implementation.
        /// </summary>
        /// <param name="message"></param>
        public void Log(string message)
        {
#if UNITY_5_3_OR_NEWER
            Debug.Log(message);
#else
        Console.WriteLine(message);
#endif
        }

        public bool IsValidLocation(uint parentId, uint zoneId)
        {
            return (parentId != 0xffffffff && parentId != 0 &&
                    zoneId != 0xffffffff && zoneId != 0);
        }

        public void AddDOToTables(IDistributedObject distObj, uint parentId = 0, uint zoneId = 0, bool ownerView = false)
        {
            if (!ownerView)
            {
                parentId = parentId != 0 ? parentId : distObj.parentId;
                zoneId = zoneId != 0 ? zoneId : distObj.zoneId;
            }

            Dictionary<uint, IDistributedObject> doTable = ownerView ? doId2ownerView : doId2do;

            // Make sure the object is not already present.
            if (doTable.ContainsKey(distObj.doId))
            {
                string tableName = ownerView ? "doId2ownerView" : "doId2do";
                Log($"doId {distObj.doId} already in {tableName} [{distObj.dclass.get_name()} stomping {doTable[distObj.doId].dclass.get_name()}]");
            }

            doTable[distObj.doId] = distObj;

            if (!ownerView && IsValidLocation(parentId, zoneId))
            {
                StoreObjectLocation(distObj, parentId, zoneId);
            }
        }

        public void StoreObjectLocation(IDistributedObject distObj, uint parentId, uint zoneId)
        {
            uint oldParentId = distObj.parentId;
            uint oldZoneId = distObj.zoneId;

            if (oldParentId != parentId)
            {
                // Notify any existing parent that we're moving away.
                IDistributedObject oldParentObj = doId2do[oldParentId];
                if (oldParentObj != null)
                {
                    oldParentObj.HandleChildLeave(distObj, oldZoneId);
                }
                DeleteObjectLocation(distObj, oldParentId, oldZoneId);
            }
            else if (oldZoneId != zoneId)
            {
                // Remove old location
                IDistributedObject oldParentObj = doId2do[oldParentId];
                if (oldParentObj != null)
                {
                    oldParentObj.HandleChildLeaveZone(distObj, oldZoneId);
                }
                DeleteObjectLocation(distObj, oldParentId, oldZoneId);
            }
            else
            {
                // Object is already at that parent and zone.
                return;
            }

            // Add to new location.
            if (storedDoIds.Contains(distObj.doId))
            {
                Log($"storeObjectLocation({distObj.dclass.get_name()} {distObj.doId}) already in storedDoIds; duplicate generate()? or didn't clean up previous instance of DO?");
            }

            if (!storedDoTable.ContainsKey(parentId))
            {
                storedDoTable.Add(parentId, new Dictionary<uint, List<uint>>());
            }

            if (!storedDoTable[parentId].ContainsKey(zoneId))
            {
                storedDoTable[parentId].Add(zoneId, new List<uint>());
            }

            storedDoTable[parentId][zoneId].Add(distObj.doId);
            storedDoIds.Add(distObj.doId);

            // Set the new parent and zone on the object.
            distObj.parentId = parentId;
            distObj.zoneId = zoneId;

            if (oldParentId != parentId)
            {
                // Give the parent a chance to run code when a new child
                // sets location to it. For example, the parent may want to
                // scene graph reparent the child to some subnode it owns.
                IDistributedObject parentObj = doId2do[parentId];
                if (parentObj != null)
                {
                    parentObj.HandleChildArrive(distObj, zoneId);
                }
                else
                {
                    Log($"StoreObjectLocation({distObj.doId}): parent {parentId} not present");
                }
            }

            if (oldZoneId != zoneId)
            {
                IDistributedObject parentObj = doId2do[parentId];
                if (parentObj != null)
                {
                    parentObj.HandleChildArriveZone(distObj, zoneId);
                }
                else
                {
                    Log($"StoreObjectLocation({distObj.doId}): parent {parentId} not present");
                }
            }
        }

        public void DeleteObjectLocation(IDistributedObject distObj, uint parentId, uint zoneId)
        {
            if (!storedDoIds.Contains(distObj.doId))
            {
                Log($"DeleteObjectLocation({distObj.dclass.get_name()} {distObj.doId}) not in storedDoIds; duplicate delete()? or invalid previous location on a new object?");
                return;
            }

            Dictionary<uint, List<uint>> parentZoneDict = storedDoTable[parentId];
            if (parentZoneDict != null)
            {
                List<uint> zoneDoSet = parentZoneDict[zoneId];
                if (zoneDoSet != null)
                {
                    if (zoneDoSet.Contains(distObj.doId))
                    {
                        zoneDoSet.Remove(distObj.doId);
                        storedDoIds.Remove(distObj.doId);
                    }
                    else
                    {
                        Log($"DeleteObjectLocation: objId: {distObj.doId} not found");
                    }
                }
                else
                {
                    Log($"DeleteObjectLocation: zoneId: {zoneId} not found");
                }
            }
            else
            {
                Log($"DeleteObjectLocation: parentId: {parentId} not found");
            }
        }
    }
}
