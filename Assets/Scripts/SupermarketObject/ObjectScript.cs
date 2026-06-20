using UnityEngine;

/// <summary>
/// Kategori dasar barang di supermarket.
/// </summary>
public enum ObjectKind
{
    Goods,   // Barang belanjaan biasa yang dikumpulkan di Trolley
    Weapon   // Senjata yang digenggam di tangan Player
}

/// <summary>
/// Status penempatan/keberadaan barang saat ini.
/// </summary>
public enum ObjectStatus
{
    Grounded, // Objek berada bebas di tanah atau rak (bisa diambil)
    Taken     // Objek sudah berada di dalam Trolley atau digenggam (tidak bisa diambil lagi)
}

/// <summary>
/// Script ini ditempelkan pada setiap prefab atau objek fisik barang di supermarket.
/// Berperan sebagai jembatan antara metadata statis (ScriptableObject) dengan keadaan fisik dinamis objek tersebut di scene.
/// </summary>
public class ObjectScript : MonoBehaviour
{
    [Header("Data Template Reference")]
    [Tooltip("Aset ScriptableObject yang menyimpan konfigurasi statis (berat, nama, jenis) barang ini.")]
    [SerializeField] private SupermarketObjectData objectData;

    [Header("Dynamic Instance State")]
    [Tooltip("Status keberadaan objek spesifik ini secara realtime. (Grounded / Taken).")]
    [SerializeField] private ObjectStatus currentStatus = ObjectStatus.Grounded;

    private void Start()
    {
        // LOGIC DI BALIK LAYAR:
        // Saat game dimulai, inisialisasi status objek menggunakan status default yang terdefinisi di ScriptableObject.
        if (objectData != null)
        {
            currentStatus = objectData.defaultStatus;
        }
    }

    // ==========================================
    // Properties Pendukung untuk Akses Eksternal
    // ==========================================

    /// <summary>
    /// Mengakses data template ScriptableObject secara langsung.
    /// </summary>
    public SupermarketObjectData ObjectData => objectData;

    /// <summary>
    /// Status aktif barang saat ini (bisa dirubah oleh trigger TrolleyArea atau genggaman Player).
    /// </summary>
    public ObjectStatus Status
    {
        get => currentStatus;
        set => currentStatus = value;
    }

    /// <summary>
    /// Berat barang bawaan. Diambil langsung dari data statis ScriptableObject.
    /// </summary>
    public float ObjWeight => objectData != null ? objectData.objWeight : 0f;

    /// <summary>
    /// Nama barang. Diambil langsung dari data statis ScriptableObject.
    /// </summary>
    public string ObjName => objectData != null ? objectData.objName : name;

    /// <summary>
    /// Jenis barang (Goods/Weapon). Diambil langsung dari data statis ScriptableObject.
    /// </summary>
    public ObjectKind KindOfObject => objectData != null ? objectData.kindOfObject : ObjectKind.Goods;
}
