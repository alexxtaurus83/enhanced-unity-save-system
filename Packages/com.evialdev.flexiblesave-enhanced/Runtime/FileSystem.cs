using System.IO;

namespace FlexibleSave.Enhanced
{
    internal interface IFileSystem
    {
        bool Exists(string path);
        void CreateDirectory(string path);
        byte[] ReadAllBytes(string path);
        void WriteAllBytes(string path, byte[] bytes);
        void Replace(string sourceFileName, string destinationFileName, string destinationBackupFileName);
        void Move(string sourceFileName, string destinationFileName);
        void Copy(string sourceFileName, string destFileName, bool overwrite);
        void Delete(string path);
    }

    internal sealed class PhysicalFileSystem : IFileSystem
    {
        public static readonly PhysicalFileSystem Instance = new PhysicalFileSystem();

        private PhysicalFileSystem() { }

        public bool Exists(string path) => File.Exists(path);

        public void CreateDirectory(string path) => Directory.CreateDirectory(path);

        public byte[] ReadAllBytes(string path) => File.ReadAllBytes(path);

        public void WriteAllBytes(string path, byte[] bytes) => File.WriteAllBytes(path, bytes);

        public void Replace(string sourceFileName, string destinationFileName, string destinationBackupFileName)
        {
            File.Replace(sourceFileName, destinationFileName, destinationBackupFileName, ignoreMetadataErrors: true);
        }

        public void Move(string sourceFileName, string destinationFileName) => File.Move(sourceFileName, destinationFileName);

        public void Copy(string sourceFileName, string destFileName, bool overwrite) => File.Copy(sourceFileName, destFileName, overwrite);

        public void Delete(string path) => File.Delete(path);
    }
}
