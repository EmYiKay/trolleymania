using UnityEngine;

/// <summary>
/// Script ini ditempelkan pada objek 'TrolleyArea' yang memiliki BoxCollider bertipe IsTrigger.
/// Berfungsi untuk mendeteksi barang belanjaan (Goods) yang masuk ke dalam keranjang trolley,
/// merubah statusnya menjadi Taken (sehingga tidak bisa di-highlight lagi), dan menjumlahkan beratnya ke TrolleyController.
/// </summary>
public class TrolleyAreaDetector : MonoBehaviour
{
    // Menyimpan referensi ke TrolleyController untuk menambahkan/mengurangi berat secara dinamis.
    private TrolleyController trolleyController;

    private void Awake()
    {
        // LOGIC DI BALIK LAYAR:
        // Karena objek 'TrolleyArea' adalah anak langsung dari 'Trolley', kita dapat mencari
        // komponen TrolleyController di objek parent secara otomatis agar tidak memerlukan setup manual di Inspector.
        trolleyController = GetComponentInParent<TrolleyController>();

        if (trolleyController == null)
        {
            Debug.LogError("TrolleyAreaDetector: TrolleyController tidak ditemukan pada parent GameObject!");
        }
    }

    /// <summary>
    /// Dipanggil saat suatu objek memasuki area keranjang Trolley.
    /// </summary>
    private void OnTriggerEnter(Collider other)
    {
        // 1. Pastikan objek yang masuk memiliki tag "Goods"
        if (other.CompareTag("Goods"))
        {
            ObjectScript objectScript = other.GetComponent<ObjectScript>();
            
            // Fallback jika collider ada pada child object
            if (objectScript == null) objectScript = other.GetComponentInParent<ObjectScript>();
            if (objectScript == null) objectScript = other.GetComponentInChildren<ObjectScript>();

            // 2. Jika memiliki ObjectScript dan statusnya masih Grounded (di luar trolley)
            if (objectScript != null && objectScript.Status == ObjectStatus.Grounded)
            {
                // Ubah status menjadi Taken agar sistem interaksi skip objek ini dari highlight
                objectScript.Status = ObjectStatus.Taken;

                // Tambahkan berat barang ke total berat di TrolleyController
                if (trolleyController != null)
                {
                    trolleyController.currentWeight += objectScript.ObjWeight;
                    Debug.Log($"[Trolley] Barang '{objectScript.ObjName}' ditambahkan. Berat barang: {objectScript.ObjWeight} | Total Berat Trolley: {trolleyController.currentWeight}");
                }

                // LOGIC DI BALIK LAYAR (Pembaruan Progres Belanjaan):
                // Laporkan pertambahan barang ke ObjectiveManager untuk mencocokkan dengan shopping list target.
                if (ObjectiveManager.Instance != null)
                {
                    ObjectiveManager.Instance.UpdateObjectiveProgress(objectScript.ObjName, 1);
                }
            }
        }
    }

    /// <summary>
    /// Dipanggil saat suatu objek keluar/terpental dari area keranjang Trolley.
    /// </summary>
    private void OnTriggerExit(Collider other)
    {
        // 1. Pastikan objek yang keluar memiliki tag "Goods"
        if (other.CompareTag("Goods"))
        {
            ObjectScript objectScript = other.GetComponent<ObjectScript>();

            if (objectScript == null) objectScript = other.GetComponentInParent<ObjectScript>();
            if (objectScript == null) objectScript = other.GetComponentInChildren<ObjectScript>();

            // 2. Jika memiliki ObjectScript dan statusnya Taken (berada di dalam trolley)
            if (objectScript != null && objectScript.Status == ObjectStatus.Taken)
            {
                // Kembalikan status menjadi Grounded agar bisa diinteraksi kembali jika berada di tanah
                objectScript.Status = ObjectStatus.Grounded;

                // Kurangi berat barang dari total berat di TrolleyController
                if (trolleyController != null)
                {
                    // Menggunakan Mathf.Max(0f, ...) untuk mencegah nilai negatif akibat presisi float (floating-point precision error)
                    trolleyController.currentWeight = Mathf.Max(0f, trolleyController.currentWeight - objectScript.ObjWeight);
                    Debug.Log($"[Trolley] Barang '{objectScript.ObjName}' keluar/dikeluarkan. Berat barang: {objectScript.ObjWeight} | Total Berat Trolley: {trolleyController.currentWeight}");

                    // LOGIC DI BALIK LAYAR (Pengecekan Tumpang Tindih Area Grab):
                    // Ketika barang belanjaan jatuh keluar dari keranjang trolley, statusnya berubah menjadi Grounded (bisa diambil).
                    // Namun karena posisinya mungkin masih berada secara fisik di dalam area jangkauan tangan/trolley (InteractArea),
                    // event OnTriggerEnter dari InteractArea tidak akan terpicu lagi (karena collidernya tidak pernah "keluar lalu masuk lagi").
                    // Solusi: Kita lakukan uji posisi instant menggunakan `Collider.ClosestPoint` pada collider milik InteractArea.
                    // Jika jarak titik terdekat kurang dari ambang batas toleransi, daftarkan kembali objek ke list highlight secara manual.
                    TrolleyInteractController interactController = trolleyController.GetComponentInChildren<TrolleyInteractController>();
                    if (interactController != null)
                    {
                        Collider interactCollider = interactController.GetComponent<Collider>();
                        if (interactCollider != null)
                        {
                            // ClosestPoint mengembalikan koordinat permukaan terdekat atau koordinat objek itu sendiri jika berada di dalam volume.
                            Vector3 closestPoint = interactCollider.ClosestPoint(other.transform.position);
                            float distanceToCollider = Vector3.Distance(closestPoint, other.transform.position);

                            // Jika jaraknya hampir 0 (sangat dekat/di dalam), daftarkan ulang sebagai kandidat grab/highlight
                            if (distanceToCollider < 0.05f)
                            {
                                Outline outline = other.GetComponent<Outline>();
                                if (outline == null) outline = other.GetComponentInParent<Outline>();
                                if (outline == null) outline = other.GetComponentInChildren<Outline>();

                                if (outline != null)
                                {
                                    interactController.TryAddCandidate(outline);
                                }
                            }
                        }
                    }
                }

                // LOGIC DI BALIK LAYAR (Pengurangan Progres Belanjaan):
                // Laporkan pengurangan barang belanjaan ke ObjectiveManager karena barang keluar dari keranjang.
                if (ObjectiveManager.Instance != null)
                {
                    ObjectiveManager.Instance.UpdateObjectiveProgress(objectScript.ObjName, -1);
                }
            }
        }
    }
}
