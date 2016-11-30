﻿using System;
using System.IO;
using System.Text;
using SharpCompress.Archives.Zip;
using OfficeExtractor.Ole;
using OpenMcdf;

/*
   Copyright 2013 - 2016 Kees van Spelde

   Licensed under The Code Project Open License (CPOL) 1.02;
   you may not use this file except in compliance with the License.
   You may obtain a copy of the License at

     http://www.codeproject.com/info/cpol10.aspx

   Unless required by applicable law or agreed to in writing, software
   distributed under the License is distributed on an "AS IS" BASIS,
   WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
   See the License for the specific language governing permissions and
   limitations under the License.
*/

namespace OfficeExtractor.Helpers
{
    /// <summary>
    /// This class contain helpers method for extraction
    /// </summary>
    internal static class Extraction
    {
        /// <summary>
        /// Default name for embedded object without a name
        /// </summary>
        public const string DefaultEmbeddedObjectName = "Embedded object";

        #region IsCompoundFile
        /// <summary>
        /// Returns true is the byte array starts with a compound file identifier
        /// </summary>
        /// <param name="bytes"></param>
        /// <returns></returns>
        public static bool IsCompoundFile(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 2)
                return false;

            return (bytes[0] == 0xD0 && bytes[1] == 0xCF);
        }
        #endregion

        #region GetFileNameFromObjectReplacementFile
        /// <summary>
        /// Tries to extracts the original filename for the OLE object out of the ObjectReplacement file
        /// </summary>
        /// <param name="zipFile"></param>
        /// <param name="index"></param>
        /// <returns></returns>
        internal static string GetFileNameFromObjectReplacementFile(SharpCompress.Archives.IArchiveEntry zipEntry)
        {
            try
            {
                using (var zipEntryStream = zipEntry.OpenEntryStream())
                using (var zipEntryMemoryStream = new MemoryStream())
                {
                    zipEntryStream.CopyTo(zipEntryMemoryStream);
                    zipEntryMemoryStream.Position = 0x4470;
                    using (var binaryReader = new BinaryReader(zipEntryMemoryStream))
                    {
                        while (binaryReader.BaseStream.Position != binaryReader.BaseStream.Length)
                        {
                            var value = binaryReader.ReadUInt16();

                            // We have found the start position from where we are going to read
                            // the original filename
                            if (value != 0x8000 || binaryReader.PeekChar() != 0x46) continue;
                            // Skip the peeked char
                            zipEntryMemoryStream.Position += 2;

                            // Read until we find the next 0x46 value
                            while (binaryReader.BaseStream.Position != binaryReader.BaseStream.Length)
                            {
                                value = binaryReader.ReadUInt16();
                                if (value != 0x46) continue;
                                // Skip the next 6 bytes
                                binaryReader.ReadBytes(6);

                                // Get the length of name string
                                var length = binaryReader.ReadUInt16();

                                // Skip the next 2 bytes
                                zipEntryMemoryStream.Position += 2;

                                // Read the filename bytes
                                var fileNameBytes = binaryReader.ReadBytes(length);
                                var fileName = Encoding.Unicode.GetString(fileNameBytes);
                                fileName = fileName.Replace("\0", string.Empty);
                                return fileName;
                            }
                        }
                    }
                }
            }
            catch
            {
                return null;
            }

            return null;
        }
        #endregion

        #region SaveFromStorageNode
        /// <summary>
        /// This method will extract and save the data from the given <see cref="CompoundFile"/> node to the <see cref="outputFolder"/>
        /// </summary>
        /// <param name="bytes">The <see cref="CompoundFile"/> as a byte array</param>
        /// <param name="outputFolder">The outputFolder</param>
        /// <returns></returns>
        /// <exception cref="Exceptions.OEFileIsPasswordProtected">Raised when a WordDocument, WorkBook or PowerPoint Document stream is password protected</exception>
        internal static string SaveFromStorageNode(byte[] bytes, string outputFolder)
        {
            using (var memoryStream = new MemoryStream(bytes))
            using (var compoundFile = new CompoundFile(memoryStream))
                return SaveFromStorageNode(compoundFile.RootStorage, outputFolder, null);
        }

        /// <summary>
        /// This method will extract and save the data from the given <see cref="CompoundFile"/> node to the <see cref="outputFolder"/>
        /// </summary>
        /// <param name="bytes">The <see cref="CompoundFile"/> as a byte array</param>
        /// <param name="outputFolder">The outputFolder</param>
        /// <param name="fileName">The fileName to use, null when the fileName is unknown</param>
        /// <returns></returns>
        /// <exception cref="Exceptions.OEFileIsPasswordProtected">Raised when a WordDocument, WorkBook or PowerPoint Document stream is password protected</exception>
        internal static string SaveFromStorageNode(byte[] bytes, string outputFolder, string fileName)
        {
            using (var memoryStream = new MemoryStream(bytes))
            using (var compoundFile = new CompoundFile(memoryStream))
                return SaveFromStorageNode(compoundFile.RootStorage, outputFolder, fileName);
        }

        /// <summary>
        /// This method will extract and save the data from the given <see cref="storage"/> node to the <see cref="outputFolder"/>
        /// </summary>
        /// <param name="storage">The <see cref="CFStorage"/> node</param>
        /// <param name="outputFolder">The outputFolder</param>
        /// <returns></returns>
        /// <exception cref="Exceptions.OEFileIsPasswordProtected">Raised when a WordDocument, WorkBook or PowerPoint Document stream is password protected</exception>
        internal static string SaveFromStorageNode(CFStorage storage, string outputFolder)
        {
            return SaveFromStorageNode(storage, outputFolder, null);
        }

        /// <summary>
        /// This method will extract and save the data from the given <see cref="storage"/> node to the <see cref="outputFolder"/>
        /// </summary>
        /// <param name="storage">The <see cref="CFStorage"/> node</param>
        /// <param name="outputFolder">The outputFolder</param>
        /// <param name="fileName">The fileName to use, null when the fileName is unknown</param>
        /// <returns></returns>
        /// <exception cref="Exceptions.OEFileIsPasswordProtected">Raised when a WordDocument, WorkBook or PowerPoint Document stream is password protected</exception>
        public static string SaveFromStorageNode(CFStorage storage, string outputFolder, string fileName)
        {
            var contents = storage.TryGetStream("CONTENTS");
            if (contents != null)
            {
                if (contents.Size <= 0) return null;
                if (string.IsNullOrWhiteSpace(fileName)) fileName = DefaultEmbeddedObjectName;
                return SaveByteArrayToFile(contents.GetData(), Path.Combine(outputFolder, fileName));
            }

            var package = storage.TryGetStream("Package");
            if (package != null)
            {
                if (package.Size <= 0) return null;
                if (string.IsNullOrWhiteSpace(fileName)) fileName = DefaultEmbeddedObjectName;
                return SaveByteArrayToFile(package.GetData(), Path.Combine(outputFolder, fileName));
            }

            var embeddedOdf = storage.TryGetStream("EmbeddedOdf");
            if (embeddedOdf != null)
            {
                // The embedded object is an Embedded ODF file
                if (embeddedOdf.Size <= 0) return null;
                if (string.IsNullOrWhiteSpace(fileName)) fileName = DefaultEmbeddedObjectName;
                return SaveByteArrayToFile(embeddedOdf.GetData(), Path.Combine(outputFolder, fileName));
            }

            if (storage.TryGetStream("\x0001Ole10Native") != null)
            {
                var ole10Native = new Ole10Native(storage);
                return ole10Native.Format == OleFormat.File
                    ? SaveByteArrayToFile(ole10Native.NativeData, Path.Combine(outputFolder, ole10Native.FileName))
                    : null;
            }

            if (storage.TryGetStream("WordDocument") != null)
            {
                // The embedded object is a Word file
                if (string.IsNullOrWhiteSpace(fileName)) fileName = "Embedded Word document.doc";
                return SaveStorageTreeToCompoundFile(storage, Path.Combine(outputFolder, fileName));
            }
            
            if (storage.TryGetStream("Workbook") != null)
            {
                // The embedded object is an Excel file   
                if (string.IsNullOrWhiteSpace(fileName)) fileName = "Embedded Excel document.xls";
                Excel.SetWorkbookVisibility(storage);
                return SaveStorageTreeToCompoundFile(storage, Path.Combine(outputFolder, fileName));
            }
            
            if (storage.TryGetStream("PowerPoint Document") != null)
            {
                // The embedded object is a PowerPoint file
                if (string.IsNullOrWhiteSpace(fileName)) fileName = "Embedded PowerPoint document.ppt";
                return SaveStorageTreeToCompoundFile(storage, Path.Combine(outputFolder, fileName));
            }
            
            return null;
        }
        #endregion

        #region SaveStorageTreeToCompoundFile
        /// <summary>
        /// This will save the complete tree from the given <paramref name="storage"/> to a new <see cref="CompoundFile"/>
        /// </summary>
        /// <param name="storage"></param>
        /// <param name="fileName">The filename with path for the new compound file</param>
        internal static string SaveStorageTreeToCompoundFile(CFStorage storage, string fileName)
        {
            fileName = FileManager.FileExistsMakeNew(fileName);

            using (var compoundFile = new CompoundFile())
            {
                GetStorageChain(compoundFile.RootStorage, storage);
                compoundFile.Save(fileName);
            }

            return fileName;
        }

        /// <summary>
        /// Returns the complete tree with all the <see cref="CFStorage"/> and <see cref="CFStream"/> children
        /// </summary>
        /// <param name="rootStorage"></param>
        /// <param name="storage"></param>
        private static void GetStorageChain(CFStorage rootStorage, CFStorage storage)
        {
            Action<CFItem> entries = item =>
            {
                if (item.IsStorage)
                {
                    var newRootStorage = rootStorage.AddStorage(item.Name);
                    GetStorageChain(newRootStorage, item as CFStorage);
                }
                else if (item.IsStream)
                {
                    var childStream = item as CFStream;
                    if (childStream == null) return;
                    var stream = rootStorage.AddStream(item.Name);
                    var bytes = childStream.GetData();
                    stream.SetData(bytes);
                }
            };

            storage.VisitEntries(entries, false);
        }
        #endregion

        #region SaveByteArrayToFile
        /// <summary>
        /// Saves the <paramref name="data"/> byte array to the <paramref name="outputFile"/>
        /// </summary>
        /// <param name="data">The stream as byte array</param>
        /// <param name="outputFile">The output filename with path</param>
        /// <returns></returns>
        /// <exception cref="OfficeExtractor.Exceptions.OEFileIsCorrupt">Raised when the file is corrupt</exception> 
        internal static string SaveByteArrayToFile(byte[] data, string outputFile)
        {
            // Because the data is stored in a stream we have no name for it so we
            // have to check the magic bytes to see with what kind of file we are dealing

            var extension = Path.GetExtension(outputFile);

            if (string.IsNullOrEmpty(extension))
            {
                var fileType = FileTypeSelector.GetFileTypeFileInfo(data);
                if (fileType != null && !string.IsNullOrEmpty(fileType.Extension))
                    outputFile += "." + fileType.Extension;

                if (fileType != null)
                    extension = "." + fileType.Extension;
            }

            // Check if the output file already exists and if so make a new one
            outputFile = FileManager.FileExistsMakeNew(outputFile);

            if (extension != null)
            {
                switch (extension.ToUpperInvariant())
                {
                    case ".XLS":
                    case ".XLT":
                    case ".XLW":
                        using (var memoryStream = new MemoryStream(data))
                        using (var compoundFile = new CompoundFile(memoryStream))
                        {
                            Excel.SetWorkbookVisibility(compoundFile.RootStorage);
                            compoundFile.Save(outputFile);
                        }
                        break;

                    case ".XLSB":
                    case ".XLSM":
                    case ".XLSX":
                    case ".XLTM":
                    case ".XLTX":
                        using (var memoryStream = new MemoryStream(data))
                        {
                            var file = Excel.SetWorkbookVisibility(memoryStream);
                            File.WriteAllBytes(outputFile, file.ToArray());
                        }
                        break;

                    default:
                        File.WriteAllBytes(outputFile, data);
                        break;
                }
            }
            else
                File.WriteAllBytes(outputFile, data);

            return outputFile;
        }
        #endregion
    }
}
