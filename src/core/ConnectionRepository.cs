using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Reflection;
using System.Text;

using astron.distributed;
using astron.util;

#if UNITY_5_3_OR_NEWER
using UnityEngine;
#endif

namespace astron.core
{
    public class ConnectionRepository : DoManager
    {
        public event Action onConnectEvent;
        public event Action<int, string> onConnectFailedEvent;
        public event Action onConnectLostEvent;

        public Dictionary<string, DCClass> dclassesByName = new Dictionary<string, DCClass>();
        public Dictionary<int, DCClass> dclassesByNumber = new Dictionary<int, DCClass>();

        protected DCFile dcFile;
        protected uint dcHash;
        protected string dcSuffix;
        protected bool clientDatagram;
        protected bool hasOwnerView;
        protected ulong msgSender;

        protected Dictionary<string, Type> dcImportsType = new Dictionary<string, Type>();
#if UNITY_5_3_OR_NEWER
        protected Dictionary<string, GameObject> dcImportsPrefab = new Dictionary<string, GameObject>();
#endif

        private TcpClient socket;
        private NetworkStream socketStream;

        public ConnectionRepository()
        {
            dcFile = new DCFile();

            // This is the string that is appended to symbols read from the
            // DC file. Servers will redefine this to 'AI'.
            dcSuffix = "";

            // Whether we're handling incoming datagrams as a client or as
            // a server (message director). In the case of the server, the
            // payload has some extra data prefixed that we have to deal with.
            clientDatagram = true;

            // Whether we're supporting 'owner' views of distributed objects
            // (i.e. 'receives ownrecv', 'I own this object and have a separate 
            // view of it regardless of where it currently is located')
            hasOwnerView = false;
        }

        public bool HasOwnerView()
        {
            return hasOwnerView;
        }

        public uint GetHash()
        {
            return dcHash;
        }

        public ulong GetMsgSender()
        {
            return msgSender;
        }

        public void Connect(string host, int port)
        {
            socket = new TcpClient();

            try
            {
                socket.Connect(host, port);
            }
            catch (SocketException e)
            {
                Log(e.Message);
                onConnectFailedEvent?.Invoke(e.ErrorCode, e.Message);
                return;
            }

            socketStream = socket.GetStream();

            BeginRead();

            onConnectEvent?.Invoke();
        }

        private void BeginRead()
        {
            // Read a 16-bit length header.
            byte[] sizeBuffer = new byte[2];
            socketStream.BeginRead(sizeBuffer, 0, 2, (result) =>
            {
                int size = (sizeBuffer[1] << 8 | sizeBuffer[0]);
                byte[] messageBuffer = new byte[size];

                // Read in the remainder of the message.
                socketStream.BeginRead(messageBuffer, 0, size, (result2) =>
                {
                    ProcessDatagram(new DatagramIterator(messageBuffer));
                    BeginRead();
                }, socket);
            }, socket);
        }

        private void ProcessDatagram(DatagramIterator di)
        {
            if (!clientDatagram)
            {
                // TODO.
                byte channelCount = di.ReadUint8();
                for (byte i = 0; i < channelCount; i++)
                {
                    ulong channel = di.ReadUint64();
                }

                // Update the message sender.
                msgSender = di.ReadUint64();
            }

            HandleDatagram(di);
        }

        /// <summary>
        /// Handle an incoming datagram.
        /// </summary>
        /// <param name="di"></param>
        protected virtual void HandleDatagram(DatagramIterator di)
        {
        }

        public void Send(Datagram dg)
        {
            int size = dg.GetSize();
            if (size > 0)
            {
                // TODO: Doing temp memory allocations like this is bad. This is a temp hack.
                byte[] outBuf = new byte[size + 2];

                // Write the uint16 length header.
                outBuf[0] = (byte)size;
                outBuf[1] = (byte)(size >> 8);

                // Copy the datagram data (offset by length size).
                Array.Copy(dg.GetData(), 0, outBuf, 2, dg.GetSize());

                // Send it.
                socketStream.Write(outBuf, 0, size + 2);
            }
        }

        private PropertyInfo GetProperty(object src, string propName)
        {
            return src.GetType().GetProperty(propName);
        }

        private MethodInfo GetMethod(object src, string methodName)
        {
            return src.GetType().GetMethod(methodName);
        }

        public Datagram ClientFormatUpdate(DCClass dclass, string fieldName, uint doId, params object[] args)
        {
            DCField field = dclass.get_field_by_name(fieldName);
            if (field == null)
            {
                Log($"No field named {fieldName} in class {dclass.get_name()}");
                return new Datagram();
            }

            DCPacker packer = new DCPacker();

            packer.raw_pack_uint16((ushort)MsgTypes.CLIENT_OBJECT_SET_FIELD);
            packer.raw_pack_uint32(doId);
            packer.raw_pack_uint16((ushort)field.get_number());

            packer.begin_pack(field);
            PackObject(packer, args);
            if (!packer.end_pack())
            {
                Log($"Failed to pack");
                return new Datagram();
            }

            return new Datagram(packer.get_bytes().ToArray(), (int)packer.get_length());
        }

        public Datagram AiFormatGenerate(IDistributedObject distObj, uint doId, uint parentId, uint zoneId, ulong targetChannel, ulong fromChannel)
        {
            DCClass dclass = distObj.dclass;
            DCPacker packer = new DCPacker();

            packer.raw_pack_uint8(1);
            packer.raw_pack_uint64(targetChannel);
            packer.raw_pack_uint64(fromChannel);

            bool optionalFields = false; // TODO.
            packer.raw_pack_uint16(optionalFields ? (ushort)MsgTypes.STATESERVER_CREATE_OBJECT_WITH_REQUIRED_OTHER
                                                  : (ushort)MsgTypes.STATESERVER_CREATE_OBJECT_WITH_REQUIRED);

            packer.raw_pack_uint32(doId);
            // Parent is a bit overloaded; this parent is not about inheritance, this
            // one is about the visibility container parent, i.e.  the zone parent:
            packer.raw_pack_uint32(parentId);
            packer.raw_pack_uint32(zoneId);
            packer.raw_pack_uint16((ushort)dclass.get_number());

            // Specify all of the required fields.
            int numFields = dclass.get_num_inherited_fields();
            for (int i = 0; i < numFields; ++i)
            {
                DCField field = dclass.get_inherited_field(i);
                if (field.is_required() && field.as_molecular_field() == null)
                {
                    packer.begin_pack(field);
                    if (!PackRequiredField(packer, dclass, field, distObj))
                    {
                        return new Datagram();
                    }
                    packer.end_pack();
                }
            }

            return new Datagram(packer.get_bytes().ToArray(), (int)packer.get_length());
        }

        public Datagram AiFormatUpdate(DCField field, uint doId, ulong toId, ulong fromId, params object[] args)
        {
            DCPacker packer = new DCPacker();

            packer.raw_pack_uint8(1);
            packer.raw_pack_uint64(toId);
            packer.raw_pack_uint64(fromId);
            packer.raw_pack_uint16((ushort)MsgTypes.STATESERVER_OBJECT_SET_FIELD);
            packer.raw_pack_uint32(doId);
            packer.raw_pack_uint16((ushort)field.get_number());

            packer.begin_pack(field);
            PackObject(packer, args);
            if (!packer.end_pack())
            {
                Log($"Failed to pack");
                return new Datagram();
            }

            return new Datagram(packer.get_bytes().ToArray(), (int)packer.get_length());
        }

        protected void HandleUpdateField(DatagramIterator di)
        {
            uint doId = di.ReadUint32();

            if (!doId2do.TryGetValue(doId, out IDistributedObject distObj))
            {
                Log($"Attempted to set field on non-existent doId: {doId}");
                return;
            }

            DCClass dclass = distObj.dclass;

            // TODO: Quite zone.
            // If in quiet zone mode, throw update away unless distobj has
            // 'neverDisable' attribute set to non-zero
            if (false)
            {
                if (!distObj.neverDisable)
                {
                    // In quiet zone and distobj is disable-able drop update on the
                    // floor
                    return;
                }
            }

            // Receive the update on the distObj using reflection.
            ReceiveUpdate(dclass, distObj, di);
        }

        private void ReceiveUpdate(DCClass dclass, IDistributedObject distObj, DatagramIterator di)
        {
            DCPacker packer = new DCPacker();
            packer.set_unpack_data(new VectorUchar(Encoding.UTF8.GetBytes(di.GetRemainingData())));

            ushort fieldId = (ushort)packer.raw_unpack_uint16();
            DCField field = dclass.get_field_by_index(fieldId);
            if (field == null)
            {
                Log($"Received update for field {fieldId}, not in class {dclass.get_name()}");
                return;
            }

            packer.begin_unpack(field);

            if (field.as_parameter() != null)
            {
                // If it's a parameter-type field, just store a new value on the object.
                object value = UnpackArgs(packer, di);
                GetProperty(distObj, field.get_name()).SetValue(distObj, value);
            }
            else
            {
                // Otherwise, it must be an atomic or molecular field, so call the
                // corresponding method.

                // Bit of a hack for C#, but lets us follow naming conventions for method names.
                StringBuilder methodNameBuilder = new StringBuilder(field.get_name());
                methodNameBuilder[0] = char.ToUpper(methodNameBuilder[0]);
                string methodName = methodNameBuilder.ToString();

                MethodInfo method = GetMethod(distObj, methodName);
                if (method == null)
                {
                    // If there's no C# method to receive this message, don't bother
                    // unpacking it, just skip past the message.
                    packer.unpack_skip();
                }
                else
                {
                    // Otherwise, unpack the args and call the C# method.
                    object[] args = UnpackArgs(packer, di);
                    method.Invoke(distObj, args);
                }
            }

            packer.end_unpack();
        }

        private object[] UnpackArgs(DCPacker packer, DatagramIterator di)
        {
            List<object> returnObj = new List<object>();
            DCPackType packType = packer.get_pack_type();

            switch (packType)
            {
                case DCPackType.PT_invalid:
                    packer.unpack_skip();
                    break;
                case DCPackType.PT_double:
                    returnObj.Add(packer.unpack_double());
                    break;
                case DCPackType.PT_int:
                    returnObj.Add(packer.unpack_int());
                    break;
                case DCPackType.PT_uint:
                    returnObj.Add(packer.unpack_uint());
                    break;
                case DCPackType.PT_int64:
                    returnObj.Add(packer.unpack_int64());
                    break;
                case DCPackType.PT_uint64:
                    returnObj.Add(packer.unpack_uint64());
                    break;
                case DCPackType.PT_string:
                    returnObj.Add(packer.unpack_string());
                    break;
                default:
                    {
                        packer.push();
                        while (packer.more_nested_fields())
                        {
                            object[] args = UnpackArgs(packer, di);
                            returnObj.AddRange(args);
                        }
                        packer.pop();
                    }
                    break;
            }

            return returnObj.ToArray();
        }

        private void PackObject(DCPacker packer, params object[] args)
        {
            DCPackType packType = packer.get_pack_type();

            switch (packType)
            {
                case DCPackType.PT_int64:
                    packer.pack_int64((long)args[0]);
                    return;
                case DCPackType.PT_uint64:
                    packer.pack_uint64((ulong)args[0]);
                    return;
                case DCPackType.PT_int:
                    packer.pack_int((int)args[0]);
                    return;
                case DCPackType.PT_uint:
                    packer.pack_uint((uint)args[0]);
                    return;
                case DCPackType.PT_double:
                    packer.pack_double((double)args[0]);
                    return;
                case DCPackType.PT_string:
                    packer.pack_string((string)args[0]);
                    return;
                case DCPackType.PT_blob:
                    packer.pack_blob((VectorUchar)args[0]);
                    return;
                default:
                    break;
            }

            bool isSequence = args.Length >= 1;
            bool isInstance = false;

            DCClass dclass = null;
            DCPackerInterface currentField = packer.get_current_field();
            if (currentField != null)
            {
                DCClassParameter classParam = packer.get_current_field().as_class_parameter();
                if (classParam != null)
                {
                    dclass = classParam.get_class();
                }
            }

            // If dclass is not NULL, the packer is expecting a class object. There
            // are then two cases: (1) the user has supplied a matching class object,
            // or (2) the user has supplied a sequence object. Unfortunately, it may
            // be difficult to differentiate these two cases, since a class object may
            // also be a sequence object.

            // The rule to differentiate them is:

            // (1) If the supplied class object is an instance of the expected class
            // object, it is considered to be a class object.

            // (2) Otherwise, if the supplied class object has a __len__() method
            // (i.e.  PySequence_Check() returns true), then it is considered to be a
            // sequence.

            // (3) Otherwise, it is considered to be a class object.

            if (dclass != null && (isInstance || !isSequence))
            {
                // The supplied object is either an instance of the expected class
                // object, or it is not a sequence--this is case (1) or (3).
                Log($"TODO: Pack class object.");
            }
            else
            {
                // The supplied object is not an instance of the expected class object,
                // but it is a sequence. This is case (2).
                packer.push();
                for (int i = 0; i < args.Length; ++i)
                {
                    PackObject(packer, args[i]);
                }
                packer.pop();
            }
        }

        private void PackClassObject(DCPacker packer, DCClass dclass, params object[] args)
        {
            packer.push();
            while (packer.more_nested_fields() && !packer.had_pack_error())
            {
                DCField field = packer.get_current_field().as_field();
                GetClassElement(packer, dclass, field, args);
            }
            packer.pop();
        }

        private void GetClassElement(DCPacker packer, DCClass dclass, DCField field, params object[] args)
        {
            string fieldName = field.get_name();
            DCPackType packType = packer.get_pack_type();

            if (string.IsNullOrEmpty(fieldName))
            {
                switch (packType)
                {
                    case DCPackType.PT_class:
                    case DCPackType.PT_switch:
                        // If the field has no name, but it is one of these container objects,
                        // we want to get its nested objects directly from the class.
                        packer.push();
                        while (packer.more_nested_fields() && !packer.had_pack_error())
                        {
                            DCField dcField = packer.get_current_field().as_field();
                            GetClassElement(packer, dclass, dcField, args);
                        }
                        packer.pop();
                        break;
                    default:
                        // Otherwise, we just pack the default value.
                        packer.pack_default_value();
                        break;
                }
            }
            else
            {
                // If the field does have a name, we will want to get it from the class
                // and pack it.
                PackRequiredField(packer, dclass, field, args);
            }
        }

        private bool PackRequiredField(DCPacker packer, DCClass dclass, DCField field, params object[] distObj)
        {
            DCParameter parameter = field.as_parameter();
            if (parameter != null)
            {
                // This is the easy case: to pack a parameter, we just look on the class
                // object for the data element.
                string fieldName = field.get_name();
                object result = GetProperty(distObj[0], fieldName).GetValue(distObj[0], null);

                if (result == null)
                {
                    // If the attribute is not defined, but the field has a default value
                    // specified, quietly pack the default value.
                    if (field.has_default_value())
                    {
                        packer.pack_default_value();
                        return true;
                    }

                    Log($"Data element {fieldName}, required by dc file for dclass {dclass.get_name()}, not defined on object.");
                    return false;
                }
                else
                {
                    // Now pack the value into the datagram.
                    PackObject(packer, result);
                    return packer.had_error();
                }
            }

            if (field.as_molecular_field() != null)
            {
                Log($"Cannot pack molecular field {field.get_name()} for generate");
                return false;
            }

            DCAtomicField atom = field.as_atomic_field();

            // We need to get the initial value of this field. There isn't a good,
            // robust way to get this; presently, we just mangle the "setFoo()" name of
            // the required field into "getFoo()" and call that.
            string setterName = atom.get_name();

            if (string.IsNullOrEmpty(setterName))
            {
                Log($"Required field is unnamed!");
                return false;
            }

            if (atom.get_num_elements() == 0)
            {
                // It sure doesn't make sense to have a required field with no parameters.
                // What data, exactly, is required?
                Log($"Required field {setterName} has no parameters!");
                return false;
            }

            StringBuilder getterName = new StringBuilder(setterName);
            if (setterName.Substring(0, 3) == "set")
            {
                // If the original method started with "set", we mangle this directly to
                // "get". Notice the capital 'G', in order to comply with C# conventions.
                getterName[0] = 'G';
            }
            else
            {
                // Otherwise, we add a "Get" prefix, and capitalize the next letter.
                getterName.Insert(0, "Get");
                getterName[3] = char.ToUpper(getterName[3]);
            }

            // Now we have to look up the getter on the distributed object and call it.
            MethodInfo func = GetMethod(distObj[0], getterName.ToString());
            if (func == null)
            {
                // As above, if there's no getter but the field has a default value
                // specified, quietly pack the default value.
                if (field.has_default_value())
                {
                    packer.pack_default_value();
                    return true;
                }

                // Otherwise, with no default value it's an error.
                Log($"Distributed class {dclass.get_name()} doesn't have getter named {getterName} to match required field {setterName}");
                return false;
            }

            object funcResult = func.Invoke(distObj[0], null);
            if (funcResult == null)
            {
                // It's possible that the function is void for whatever reason.
                Log($"Distributed class {dclass.get_name()} doesn't have getter named {getterName} that returns a non-void value");
                return false;
            }

            // Now pack the value into the datagram.
            PackObject(packer, funcResult);
            return packer.had_error();
        }

        /// <summary>
        /// Generate a distributed object global (UD).
        /// </summary>
        /// <param name="doId"></param>
        /// <param name="dcName"></param>
        public IDistributedObject GenerateGlobalObject(uint doId, string dcName)
        {
            // Look up the dclass.
            string suffix = dcSuffix;
            DCClass dclass;
            if (!dclassesByName.TryGetValue(dcName + suffix, out dclass))
            {
                Log($"Need to define {dcName + suffix}");

                suffix = "AI";
                if (!dclassesByName.TryGetValue(dcName + suffix, out dclass))
                {
                    suffix = "";
                    dclassesByName.TryGetValue(dcName, out dclass);
                }
            }

            // Create a new distributed object, and put it in the dictionary.
            IDistributedObject distObj;

            // Try instantiate the dclass type.
            if (dcImportsType.TryGetValue(dclass.get_name() + suffix, out Type doType))
            {
                distObj = (IDistributedObject)Activator.CreateInstance(doType);
            }
#if UNITY_5_3_OR_NEWER
            else if (dcImportsPrefab.TryGetValue(dclass.get_name() + suffix, out GameObject doPrefab))
            {
                GameObject sceneObj = UnityEngine.Object.Instantiate(doPrefab);
                distObj = sceneObj.GetComponent<IDistributedObject>();
            }
#endif
            else
            {
                Log($"Could not create an undefined {dclass.get_name() + suffix} object.");
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
            distObj.AnnounceGenerate();
            distObj.parentId = 0;
            distObj.zoneId = 0;

            return distObj;
        }

        /// <summary>
        /// Reads in an array of dc files.
        /// </summary>
        /// <param name="dcFileNames"></param>
        public void ReadDCFile(string[] dcFileNames)
        {
            // Reset the dc file object.
            dcFile.clear();

            // Read the dc files.
            foreach (string dcFileName in dcFileNames)
            {
                bool read = dcFile.read(dcFileName);
                string outString = read ? $"DCFile::read of {Path.GetFileName(dcFileName)}"
                                        : $"Could not read dc file: {dcFileName}";
                Log(outString);
            }

            dcHash = dcFile.get_hash();

            // Import all of the modules required by the DC file(s).
            for (int i = 0; i < dcFile.get_num_import_modules(); i++)
            {
                string moduleImport = dcFile.get_import_module(i);

                // The module name may be represented as "ModuleName/AI/UD".
                string[] suffix = moduleImport.Split('/');
                string moduleName = suffix[0];

                // Only import modules we 'care' about.
                // (Don't attempt to import server modules).
                if (suffix.Contains(dcSuffix))
                {
                    moduleName += dcSuffix;
                }
                else if (suffix.Contains("AI") && dcSuffix == "UD")
                {
                    moduleName += "AI";
                }

                List<string> importSymbols = new List<string>();
                for (int n = 0; n < dcFile.get_num_import_symbols(i); n++)
                {
                    string symbolImport = dcFile.get_import_symbol(i, n);

                    // The symbol name may be represented as "SymbolName/AI/UD".
                    suffix = symbolImport.Split('/');
                    string symbolName = suffix[0];

                    if (suffix.Contains(dcSuffix))
                    {
                        symbolName += dcSuffix;
                    }
                    else if (suffix.Contains("AI") && dcSuffix == "UD")
                    {
                        symbolName += "AI";
                    }

                    importSymbols.Add(symbolName);
                }

                ImportModule(moduleName, importSymbols);
            }

            // Now get the class definitions for the classes named
            // in the DC file.
            for (int i = 0; i < dcFile.get_num_classes(); i++)
            {
                DCClass dclass = dcFile.get_class(i);
                int number = dclass.get_number();
                string className = dclass.get_name() + dcSuffix;

                // Does the class have a definition defined in the newly
                // imported namespace?
                Type classTypeDef = null;
                if (!dcImportsType.TryGetValue(className, out classTypeDef) && dcSuffix == "UD")
                {
                    className = dclass.get_name() + "AI";
                    dcImportsType.TryGetValue(className, out classTypeDef);
                }

                // Also try without the dcSuffix.
                if (classTypeDef == null)
                {
                    className = dclass.get_name();
                    dcImportsType.TryGetValue(className, out classTypeDef);
                }

#if UNITY_5_3_OR_NEWER
                // If the classTypeDef is still null, it could be a prefab.
                GameObject classPrefabDef = null;
                if (classTypeDef == null && dcSuffix == "UD")
                {
                    className = dclass.get_name() + "AI";
                    dcImportsPrefab.TryGetValue(className, out classPrefabDef);
                }

                // If the classPrefabDef is still null, it could be a prefab.
                if (classTypeDef == null && classPrefabDef == null)
                {
                    className = dclass.get_name();
                    dcImportsPrefab.TryGetValue(className, out classPrefabDef);
                }

                if (classTypeDef == null && classPrefabDef == null)
                {
                    Log($"No class definition for {className}");
                }
#else
                if (classTypeDef == null)
                {
                    Log($"No class definition for {className}");
                }
#endif

                dclassesByName.Add(className, dclass);
                if (number >= 0)
                {
                    dclassesByNumber.Add(number, dclass);
                }
            }

            // Owner views.
            if (HasOwnerView())
            {
                // TODO: Owner views.
            }
        }

        /// <summary>
        /// Import a new module into the dc modules namespace.
        /// </summary>
        /// <param name="moduleName"></param>
        /// <param name="importSymbols"></param>
        private void ImportModule(string moduleName, List<string> importSymbols)
        {
            if (importSymbols.Count > 0)
            {
                // "from moduleName import symbolName, symbolName, ..."
                foreach (string symbolName in importSymbols)
                {
                    Type moduleType = Type.GetType($"{moduleName}.{symbolName}");
#if UNITY_5_3_OR_NEWER
                    GameObject moduleObject = Resources.Load($"Assets/{moduleName.Replace('.', '/')}/{symbolName}") as GameObject;

                    // Attempt to load prefab objects first.
                    // There could be a prefab with the same name as the script it uses.
                    if (moduleObject != null)
                    {
                        dcImportsPrefab.Add(symbolName, moduleObject);
                    }
                    // Next, see if the symbol exists as a standalone C# file.
                    // If it does, it shouldn't inherit MonoBehaviour.
                    else if (moduleType != null)
                    {
                        dcImportsType.Add(symbolName, moduleType);
                    }
                    // Otherwise, thrown an exception.
                    else
                    {
                        throw new Exception($"Symbol {symbolName} not defined in module {moduleName}");
                    }
#else
                    if (moduleType == null)
                    {
                        throw new Exception($"Symbol {symbolName} not defined in module {moduleName}");
                    }
                    
                     dcImportsType.Add(symbolName, moduleType);
#endif
                }
            }
            else
            {
                // "import moduleName"
                string symbolName = moduleName.Split('.').Last();

                Type moduleType = Type.GetType(moduleName);
#if UNITY_5_3_OR_NEWER
                GameObject moduleObject = Resources.Load($"Assets/{moduleName.Replace('.', '/')}") as GameObject;

                if (moduleObject != null)
                {
                    dcImportsPrefab.Add(symbolName, moduleObject);
                }
                else if (moduleType != null)
                {
                    dcImportsType.Add(symbolName, moduleType);
                }
                else
                {
                    throw new Exception($"Module not defined {symbolName}");
                }
#else
                if (moduleType == null)
                {
                    throw new Exception($"Module not defined {symbolName}");
                }

                dcImportsType.Add(symbolName, moduleType);
#endif
            }
        }
    }
}