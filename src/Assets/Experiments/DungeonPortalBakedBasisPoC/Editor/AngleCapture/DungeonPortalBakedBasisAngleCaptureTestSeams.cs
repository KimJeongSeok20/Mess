using System;

namespace DungeonPortalTransportPoC.Editor
{
    public static partial class DungeonPortalReceiverBounceBaker
    {
        public sealed class EditorSessionSnapshotTestHandle
        {
            private EditorSessionSnapshot snapshot;

            private EditorSessionSnapshotTestHandle(EditorSessionSnapshot value)
            {
                snapshot = value ?? throw new ArgumentNullException(nameof(value));
            }

            public void RestoreAndAssert()
            {
                EditorSessionSnapshot value = snapshot;
                if (value == null)
                    throw new InvalidOperationException("The editor-session test snapshot was already restored.");
                value.Restore();
                value.AssertRestoredClean();
                snapshot = null;
            }

            internal static EditorSessionSnapshotTestHandle Capture()
            {
                return new EditorSessionSnapshotTestHandle(EditorSessionSnapshot.Capture());
            }
        }

        public static EditorSessionSnapshotTestHandle CaptureEditorSessionSnapshotForTest()
        {
            return EditorSessionSnapshotTestHandle.Capture();
        }
    }
}
