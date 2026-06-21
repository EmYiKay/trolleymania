using UnityEngine;

/// <summary>
/// Script ini berfungsi untuk mengontrol pergerakan fisik Trolley (dan Player yang mendorongnya).
/// Menggunakan Rigidbody untuk memberikan efek pergerakan yang "berat" dan realistis (Need for Seat style).
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class TrolleyController : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Referensi ke script FloatingJoystick untuk mengambil data input.")]
    [SerializeField] private FloatingJoystick joystick;

    [Header("Movement Settings")]
    [Tooltip("Kecepatan maksimum trolley saat berjalan lurus.")]
    [SerializeField] private float maxSpeed = 8f;

    [Tooltip("Seberapa cepat trolley berakselerasi menuju kecepatan maksimum.")]
    [SerializeField] private float acceleration = 2f;

    [Tooltip("Seberapa cepat trolley mengerem saat joystick dilepas.")]
    [SerializeField] private float deceleration = 4f;

    [Tooltip("Batas mati input joystick (deadzone). Input di bawah nilai ini akan diabaikan.")]
    [SerializeField] private float inputDeadzone = 0.1f;

    [Header("Steering Settings")]
    [Tooltip("Kecepatan rotasi/belok dasar saat trolley bergerak lambat.")]
    [SerializeField] private float baseTurnSpeed = 90f;

    [Tooltip("Ambang batas rasio kecepatan (0-1) di mana kemudi mulai terkunci sangat berat (full speed threshold).")]
    [SerializeField] private float heavyTurnSpeedThreshold = 0.75f;

    [Tooltip("Pengali kecepatan belok saat berada di kecepatan maksimum. Semakin kecil nilainya, semakin sukar dibelokkan saat kencang (Inersia berat).")]
    [Range(0.05f, 0.8f)]
    [SerializeField] private float turnDifficultyAtMaxSpeed = 0.2f;

    [Tooltip("Sudut belok minimal untuk memicu rotasi fisik Rigidbody.")]
    [SerializeField] private float minTurnAngleThreshold = 0.0001f;

    [Header("Weight/Cargo Settings")]
    [Tooltip("Berat barang bawaan saat ini (bisa dimodifikasi oleh script lain nanti).")]
    public float currentWeight = 0f;

    [Tooltip("Seberapa besar pengaruh berat barang terhadap penurunan akselerasi dan belokan.")]
    [SerializeField] private float weightImpactMultiplier = 0.5f;

    // Variabel internal
    private Rigidbody rb;
    private float currentForwardSpeed = 0f;
    private float currentSidewaySpeed = 0f;

    // Menampung akumulasi input geser horizontal (yaw) dari layar kanan selama satu frame.
    // Akumulasi dilakukan di Update() pada script kamera dan diterapkan serta di-reset di FixedUpdate() pada script ini.
    // Metode ini mencegah input hilang akibat perbedaan frekuensi pemanggilan antara Update() dan FixedUpdate().
    private float accumulatedSwipeRotationInput = 0f;

    // ==========================================
    // Properties untuk dibaca oleh TouchCameraController
    // ==========================================

    // Mengekspos pengali kesulitan berbelok saat kecepatan maksimum ke script kamera.
    public float TurnDifficultyAtMaxSpeed => turnDifficultyAtMaxSpeed;

    // Menghitung faktor penurunan kecepatan/kemudahan putar berdasarkan berat barang bawaan saat ini.
    // Rumus: 1 / (1 + (berat * pengali_pengaruh)).
    // LOGIC DI BALIK LAYAR: Semakin besar barang bawaan, penyebut semakin besar sehingga weightFactor semakin kecil (mendekati 0).
    // Hal ini digunakan untuk mengurangi akselerasi dan daya belok secara proporsional.
    public float WeightFactor => 1f / (1f + (currentWeight * weightImpactMultiplier));

    // Menghitung rasio kecepatan saat ini terhadap kecepatan maksimum acuan (maxSpeed yang disesuaikan dengan berat).
    // LOGIC DI BALIK LAYAR: Menggunakan kecepatan dari variabel persistent (Forward & Sideway) dibanding dengan maxSpeed * WeightFactor.
    // Dibatasi antara 0 dan 1 (Mathf.Clamp01). Digunakan oleh UI, kontrol kemudi, dan detektor kerusakan (TrolleyCollisionHandler).
    public float CurrentSpeedRatio => Mathf.Clamp01(new Vector2(currentSidewaySpeed, currentForwardSpeed).magnitude / (maxSpeed * WeightFactor));

    private void Start()
    {
        rb = GetComponent<Rigidbody>();

        // Mengatur parameter Rigidbody agar sesuai dengan simulasi fisik trolley
        rb.useGravity = true;
        rb.drag = 0.1f; // Sedikit hambatan udara agar tidak seluncur tanpa henti
        rb.angularDrag = 2f; // Hambatan rotasi tinggi agar tidak berputar berlebihan (stabil)

        // PENTING: Kunci rotasi X dan Z agar trolley tidak terguling/miring saat menabrak dinding
        rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;

        // Auto-assign joystick jika kosong di Inspector demi menghindari NullReferenceException
        if (joystick == null)
        {
            joystick = FindObjectOfType<FloatingJoystick>();
        }
    }

    private void FixedUpdate()
    {
        if (joystick == null) return;

        // Ambil input arah dari joystick (X untuk kiri-kanan, Y untuk maju-mundur)
        Vector2 input = joystick.Direction;

        MoveTrolley(input);
        RotateTrolley();
    }

    /// <summary>
    /// Mengontrol akselerasi maju/mundur/kiri/kanan trolley relatif terhadap arah hadap saat ini.
    /// </summary>
    private void MoveTrolley(Vector2 input)
    {
        // Ambil faktor beban barang untuk membatasi performa akselerasi & deselerasi trolley
        float weightFactor = WeightFactor;

        // 1. Tentukan target kecepatan lokal berdasarkan input joystick (X untuk menyamping, Y untuk maju/mundur)
        //    LOGIC DI BALIK LAYAR: Top speed tidak lagi dikalikan dengan weightFactor, 
        //    sehingga trolley tetap bisa mencapai kecepatan penuh (maxSpeed) meskipun memuat banyak barang.
        float targetForwardSpeed = input.y * maxSpeed;
        float targetSidewaySpeed = input.x * maxSpeed;

        // 2. Akselerasi/Deselerasi sumbu Z Lokal (Maju - Mundur) secara persistent.
        //    LOGIC DI BALIK LAYAR: Akselerasi dan deselerasi dikalikan dengan weightFactor.
        //    Semakin berat beban barang, semakin lama waktu yang dibutuhkan trolley untuk berakselerasi 
        //    ke top speed, dan semakin lama/jauh jarak rem yang dibutuhkan untuk berhenti (efek inersia).
        if (Mathf.Abs(input.y) > inputDeadzone)
        {
            currentForwardSpeed = Mathf.MoveTowards(currentForwardSpeed, targetForwardSpeed, acceleration * weightFactor * Time.fixedDeltaTime);
        }
        else
        {
            currentForwardSpeed = Mathf.MoveTowards(currentForwardSpeed, 0f, deceleration * weightFactor * Time.fixedDeltaTime);
        }

        // 3. Akselerasi/Deselerasi sumbu X Lokal (Kiri - Kanan / Menyamping) secara persistent.
        if (Mathf.Abs(input.x) > inputDeadzone)
        {
            currentSidewaySpeed = Mathf.MoveTowards(currentSidewaySpeed, targetSidewaySpeed, acceleration * weightFactor * Time.fixedDeltaTime);
        }
        else
        {
            currentSidewaySpeed = Mathf.MoveTowards(currentSidewaySpeed, 0f, deceleration * weightFactor * Time.fixedDeltaTime);
        }

        // 4. Hitung vektor pergerakan lokal berdasarkan kecepatan persistent (transform.forward dan transform.right).
        //    Ini mengubah kecepatan lokal menjadi orientasi arah dunia (World Space) yang tepat sesuai dengan arah hadap trolley.
        Vector3 localVelocity = (transform.forward * currentForwardSpeed) + (transform.right * currentSidewaySpeed);

        // 5. Terapkan kecepatan ke Rigidbody dengan mempertahankan gravitasi pada sumbu Y (rb.velocity.y).
        rb.velocity = new Vector3(localVelocity.x, rb.velocity.y, localVelocity.z);
    }

    /// <summary>
    /// Menambahkan input rotasi horizontal dari swipe layar kanan (dipanggil oleh TouchCameraController di Update).
    /// </summary>
    public void AddSwipeRotationInput(float amount)
    {
        // Menjumlahkan delta sentuhan horizontal setiap frame rendering (Update) ke penampung sementara.
        // Ini memastikan gerakan jari sekecil apa pun diakumulasikan dengan akurat sebelum dieksekusi di FixedUpdate.
        accumulatedSwipeRotationInput += amount;
    }

    /// <summary>
    /// Mengontrol rotasi/belokan trolley berdasarkan input swipe dengan efek inersia (semakin kencang/berat semakin sulit belok).
    /// </summary>
    private void RotateTrolley()
    {
        // 1. Dapatkan rasio kecepatan saat ini (mengacu pada virtual max speed yang disesuaikan berat).
        float speedRatio = CurrentSpeedRatio;

        // 2. Tentukan pengali kemudahan putar berdasarkan kecepatan (Need for Seat style).
        // LOGIC DI BALIK LAYAR:
        // Jika kecepatan mencapai atau melebihi batas (heavyTurnSpeedThreshold), 
        // kita paksa pengali rotasi menjadi turnDifficultyAtMaxSpeed (belokan sangat berat/kaku).
        // Jika di bawah threshold, kita lakukan interpolasi linier (Lerp) secara mulus dari ringan (1.0f) ke berat.
        float speedTurnMultiplier;
        if (speedRatio >= heavyTurnSpeedThreshold)
        {
            speedTurnMultiplier = turnDifficultyAtMaxSpeed;
        }
        else
        {
            float normalizedRatio = speedRatio / heavyTurnSpeedThreshold;
            speedTurnMultiplier = Mathf.Lerp(1f, turnDifficultyAtMaxSpeed, normalizedRatio);
        }

        // 3. Ambil faktor beban barang. Semakin banyak barang, trolley semakin sukar dirubah orientasinya (berat di semua kondisi).
        float weightFactor = WeightFactor;

        // 4. Hitung sudut putar akhir (dalam derajat) untuk frame fisika ini.
        //    Sudut dihitung dari akumulasi geser layar dikali faktor reduksi kecepatan dan berat beban barang.
        //    Ini menjamin belokan terasa berat baik saat diam, berjalan lambat, maupun saat meluncur kencang.
        float turnAngle = accumulatedSwipeRotationInput * speedTurnMultiplier * weightFactor;

        // 5. Reset akumulator setelah nilainya digunakan agar tidak terjadi double-rotation pada frame berikutnya.
        accumulatedSwipeRotationInput = 0f;

        // 6. Jika terdapat perubahan sudut rotasi yang cukup signifikan
        if (Mathf.Abs(turnAngle) > minTurnAngleThreshold)
        {
            // Buat rotasi delta berupa Quaternion mengelilingi sumbu Y (horizontal).
            Quaternion turnRotation = Quaternion.Euler(0f, turnAngle, 0f);
            
            // LOGIC DI BALIK LAYAR:
            // Menggunakan rb.MoveRotation() untuk memutar Rigidbody secara fisik.
            // Rumus: rb.rotation * turnRotation mengalikan orientasi saat ini dengan delta rotasi untuk mendapatkan orientasi baru.
            // Mengapa rb.MoveRotation? Karena ini adalah metode bawaan Unity yang paling aman untuk objek ber-Rigidbody.
            // Metode ini memungkinkan mesin fisika menghitung interaksi tabrakan (collision) dengan baik sepanjang lintasan beloknya,
            // berbeda jika kita langsung memanipulasi transform.rotation secara mentah (teleportasi rotasi yang bisa menembus dinding).
            rb.MoveRotation(rb.rotation * turnRotation);
        }
    }
}
