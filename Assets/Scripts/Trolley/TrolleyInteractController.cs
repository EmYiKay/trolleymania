using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Script ini berfungsi untuk mendeteksi objek dengan tag "Weapon" atau "Goods" yang masuk ke area trigger interaksi,
/// menghitung objek mana yang paling dekat dengan Trolley secara realtime, dan memberikan efek sorotan (outline highlight)
/// pada objek terdekat tersebut untuk meningkatkan User Experience (UX) pemain.
/// </summary>
public class TrolleyInteractController : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Transform acuan untuk perhitungan jarak (biasanya bodi utama Trolley). Jika dikosongkan, akan menggunakan parent transform atau transform ini sendiri.")]
    [SerializeField] private Transform referenceTransform;

    [Header("Settings")]
    [Tooltip("Tag objek yang diperbolehkan masuk ke area interaksi.")]
    [SerializeField] private List<string> targetTags = new List<string> { "Weapon", "Goods" };

    // List internal untuk menampung semua objek target yang berada di dalam area trigger.
    // LOGIC DI BALIK LAYAR: Menggunakan hash set atau list untuk melacak objek aktif secara dinamis.
    // Setiap kali objek masuk (OnTriggerEnter), ia ditambahkan ke list ini, dan dihapus saat keluar (OnTriggerExit).
    private List<Outline> candidatesInArea = new List<Outline>();

    // Menyimpan referensi ke objek terdekat saat ini yang sedang di-highlight.
    // Digunakan untuk mendeteksi perubahan objek terdekat sehingga kita tidak melakukan ToggleOutline berulang-ulang setiap frame.
    private Outline currentHighlightedOutline = null;

    // ==========================================
    // Properties dan Method Publik untuk HUDController
    // ==========================================

    /// <summary>
    /// Mengekspos referensi ke objek terdekat yang sedang di-highlight saat ini.
    /// </summary>
    public Outline CurrentHighlightedOutline => currentHighlightedOutline;

    /// <summary>
    /// Menghapus objek dari daftar kandidat secara manual (misal setelah objek di-grab pemain).
    /// </summary>
    public void RemoveCandidate(Outline outline)
    {
        if (outline == null) return;

        // Matikan outline terlebih dahulu agar tidak terus menyala saat objek melayang/pindah
        outline.ToggleOutline(false);

        if (candidatesInArea.Contains(outline))
        {
            candidatesInArea.Remove(outline);
        }

        if (currentHighlightedOutline == outline)
        {
            currentHighlightedOutline = null;
        }
    }

    /// <summary>
    /// Mencoba mendaftarkan kembali objek secara manual ke dalam list highlight (misalnya jika objek keluar dari keranjang
    /// dan ternyata fisiknya masih berada di dalam area jangkauan interaksi tangan).
    /// </summary>
    public void TryAddCandidate(Outline outline)
    {
        if (outline == null) return;

        // Pastikan tag objek diperbolehkan
        if (targetTags.Contains(outline.tag))
        {
            // Cek apakah objek sudah berstatus Taken (di dalam trolley/tangan)
            ObjectScript objectScript = outline.GetComponent<ObjectScript>();
            if (objectScript == null) objectScript = outline.GetComponentInParent<ObjectScript>();
            if (objectScript == null) objectScript = outline.GetComponentInChildren<ObjectScript>();

            if (objectScript != null && objectScript.Status == ObjectStatus.Taken)
            {
                return; // Jangan ditambahkan jika statusnya Taken
            }

            // Tambahkan jika belum terdaftar
            if (!candidatesInArea.Contains(outline))
            {
                candidatesInArea.Add(outline);
            }
        }
    }

    private void Awake()
    {
        // LOGIC DI BALIK LAYAR:
        // Jika referenceTransform belum diatur di Inspector, cari parent terdekat sebagai acuan titik pusat.
        // Jika tidak memiliki parent, gunakan transform objek ini sendiri. Hal ini mempermudah pemasangan komponen
        // tanpa memaksa desainer level mengisi field referenceTransform secara manual.
        if (referenceTransform == null)
        {
            referenceTransform = transform.parent != null ? transform.parent : transform;
        }
    }

    private void Update()
    {
        // 1. Bersihkan list dari objek yang mungkin telah dihancurkan (destroyed) atau dinonaktifkan (inactive) di game.
        //    PENTING: Dalam Unity, menghancurkan objek yang ada di dalam List tidak otomatis menghapus slot list tersebut.
        //    Slot tersebut akan berisi referensi 'null' (Unity fake null). Jika tidak dibersihkan, hal ini memicu NullReferenceException.
        CleanupCandidates();

        // 2. Cari objek terdekat dari daftar kandidat yang ada.
        Outline closestOutline = FindClosestCandidate();

        // 3. Update highlight/outline berdasarkan hasil pencarian objek terdekat.
        UpdateHighlight(closestOutline);
    }

    /// <summary>
    /// Dipanggil oleh Unity Physics Engine ketika collider lain memasuki area Trigger objek ini.
    /// </summary>
    private void OnTriggerEnter(Collider other)
    {
        // LOGIC DI BALIK LAYAR:
        // Kita mencocokkan tag objek yang masuk. Jika tag-nya ada di daftar targetTags,
        // kita coba mendapatkan script 'Outline' pada objek tersebut (bisa langsung di objek itu, di parent, atau di child-nya).
        if (targetTags.Contains(other.tag))
        {
            // LOGIC DI BALIK LAYAR (Pencegahan Highlight Barang yang Sudah Diambil):
            // Sebelum memasukkan objek ke daftar kandidat highlight, kita periksa dulu statusnya.
            // Jika objek tersebut sudah berstatus 'Taken' (di dalam trolley), kita langsung skip agar tidak bisa di-highlight/di-grab lagi.
            ObjectScript objectScript = other.GetComponent<ObjectScript>();
            if (objectScript == null) objectScript = other.GetComponentInParent<ObjectScript>();
            if (objectScript == null) objectScript = other.GetComponentInChildren<ObjectScript>();

            if (objectScript != null && objectScript.Status == ObjectStatus.Taken)
            {
                return;
            }

            Debug.Log("nama object = " + other.name);
            Outline outline = other.GetComponent<Outline>();
            
            // Fallback: Jika Outline tidak ditemukan langsung di collider (misal collider berada di child gameobject),
            // cari di parent atau child objek tersebut.
            if (outline == null)
            {
                outline = other.GetComponentInParent<Outline>();
            }
            if (outline == null)
            {
                outline = other.GetComponentInChildren<Outline>();
            }

            // Jika script Outline ditemukan dan belum terdaftar di list kandidat kita
            if (outline != null && !candidatesInArea.Contains(outline))
            {
                candidatesInArea.Add(outline);
            }
        }
    }

    /// <summary>
    /// Dipanggil oleh Unity Physics Engine ketika collider lain keluar dari area Trigger objek ini.
    /// </summary>
    private void OnTriggerExit(Collider other)
    {
        if (targetTags.Contains(other.tag))
        {
            Outline outline = other.GetComponent<Outline>();
            
            if (outline == null)
            {
                outline = other.GetComponentInParent<Outline>();
            }
            if (outline == null)
            {
                outline = other.GetComponentInChildren<Outline>();
            }

            // Jika objek terdaftar di list kandidat, hapus dari list.
            if (outline != null && candidatesInArea.Contains(outline))
            {
                candidatesInArea.Remove(outline);
                
                // Pastikan outline objek dinonaktifkan ketika meninggalkan area interaksi
                outline.ToggleOutline(false);
            }
        }
    }

    /// <summary>
    /// Membersihkan daftar kandidat dari objek yang sudah bernilai null (misal karena telah dipickup dan dihancurkan).
    /// </summary>
    private void CleanupCandidates()
    {
        // Loop mundur (dari belakang ke depan) sangat penting ketika menghapus elemen dari list di dalam loop.
        // Jika menggunakan loop maju (0 ke Count), indeks akan bergeser saat elemen dihapus, sehingga ada elemen yang terlewat atau terjadi indeks di luar jangkauan.
        for (int i = candidatesInArea.Count - 1; i >= 0; i--)
        {
            if (candidatesInArea[i] == null || !candidatesInArea[i].gameObject.activeInHierarchy)
            {
                candidatesInArea.RemoveAt(i);
                continue;
            }

            // LOGIC DI BALIK LAYAR (Pembersihan Realtime Barang Taken):
            // Apabila barang sedang berada di dalam trigger area jangkauan tangan dan statusnya berubah menjadi 'Taken'
            // (misal karena berhasil ditarik masuk ke area trolley), kita harus segera mematikan highlight outline-nya
            // dan menghapusnya dari daftar kandidat agar tidak terpilih lagi.
            ObjectScript objectScript = candidatesInArea[i].GetComponent<ObjectScript>();
            if (objectScript == null) objectScript = candidatesInArea[i].GetComponentInParent<ObjectScript>();
            if (objectScript == null) objectScript = candidatesInArea[i].GetComponentInChildren<ObjectScript>();

            if (objectScript != null && objectScript.Status == ObjectStatus.Taken)
            {
                candidatesInArea[i].ToggleOutline(false);
                candidatesInArea.RemoveAt(i);
            }
        }
    }

    /// <summary>
    /// Mencari objek Outline terdekat dari referenceTransform berdasarkan jarak Euclidean 3D.
    /// </summary>
    private Outline FindClosestCandidate()
    {
        if (candidatesInArea.Count == 0) return null;

        Outline closest = null;
        float minDistance = float.MaxValue;
        Vector3 refPos = referenceTransform.position;

        // LOGIC DI BALIK LAYAR:
        // Kita melintasi list kandidat dan mengukur jarak masing-masing objek ke titik pusat trolley.
        // Menggunakan Vector3.SqrMagnitude sebenarnya lebih cepat dibanding Vector3.Distance karena tidak menghitung akar kuadrat (square root).
        // Namun demi kemudahan pemahaman, Vector3.Distance tetap digunakan dan performanya aman karena kapasitas list area supermarket biasanya relatif kecil.
        foreach (Outline candidate in candidatesInArea)
        {
            float dist = Vector3.Distance(refPos, candidate.transform.position);
            if (dist < minDistance)
            {
                minDistance = dist;
                closest = candidate;
            }
        }

        return closest;
    }

    /// <summary>
    /// Mengatur nyala/mati outline sesuai dengan target terdekat baru yang terdeteksi.
    /// </summary>
    private void UpdateHighlight(Outline newClosest)
    {
        // Kasus 1: Tidak ada perubahan target terdekat yang di-highlight
        if (currentHighlightedOutline == newClosest) return;

        // Kasus 2: Target terdekat berubah (atau objek lama keluar dan tidak ada pengganti)
        if (currentHighlightedOutline != null)
        {
            // Matikan highlight pada objek lama yang tidak lagi menjadi yang terdekat
            currentHighlightedOutline.ToggleOutline(false);
        }

        // Kasus 3: Menetapkan highlight pada target terdekat yang baru
        currentHighlightedOutline = newClosest;

        if (currentHighlightedOutline != null)
        {
            // Nyalakan highlight pada target terdekat yang baru
            currentHighlightedOutline.ToggleOutline(true);
        }
    }

    // Dipanggil saat komponen dinonaktifkan untuk merapikan sisa highlight yang masih aktif.
    private void OnDisable()
    {
        if (currentHighlightedOutline != null)
        {
            currentHighlightedOutline.ToggleOutline(false);
            currentHighlightedOutline = null;
        }
        candidatesInArea.Clear();
    }
}
