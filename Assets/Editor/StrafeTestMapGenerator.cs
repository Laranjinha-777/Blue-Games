// IMPORTANTE: este arquivo precisa ficar dentro de uma pasta chamada "Editor" (Assets/Editor).
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>
/// Cria um mapa de testes de movimentação + o Player já configurado na cena aberta.
/// Menu: Tools > Strafe > Criar Mapa de Teste + Player
/// Pode rodar de novo a qualquer momento: ele apaga a versão anterior e recria.
/// </summary>
public static class StrafeTestMapGenerator
{
    [MenuItem("Tools/Strafe/Criar Mapa de Teste + Player")]
    public static void Generate()
    {
        DestroyIfExists("StrafeTestMap");
        DestroyIfExists("Player");

        Transform root = new GameObject("StrafeTestMap").transform;

        Material floorMat = MakeMat("Chao", new Color(0.30f, 0.32f, 0.36f));
        Material wallMat = MakeMat("Parede", new Color(0.55f, 0.58f, 0.65f));
        Material rampMat = MakeMat("Rampa", new Color(0.85f, 0.45f, 0.20f));
        Material platMat = MakeMat("Plataforma", new Color(0.25f, 0.65f, 0.80f));
        Material markMat = MakeMat("Marcador", new Color(0.95f, 0.85f, 0.20f));

        // ---------- Chão e limites ----------
        Box(root, "Chao", new Vector3(0, -0.5f, 0), new Vector3(130, 1, 130), floorMat);
        float b = 65f;
        Box(root, "Limite N", new Vector3(0, 6, b), new Vector3(132, 12, 2), wallMat);
        Box(root, "Limite S", new Vector3(0, 6, -b), new Vector3(132, 12, 2), wallMat);
        Box(root, "Limite L", new Vector3(b, 6, 0), new Vector3(2, 12, 132), wallMat);
        Box(root, "Limite O", new Vector3(-b, 6, 0), new Vector3(2, 12, 132), wallMat);

        // ---------- Marcadores de distância (a cada 10m no eixo Z) ----------
        for (int z = -50; z <= 60; z += 10)
        {
            GameObject m = Box(root, "Marcador " + z, new Vector3(0, 0.01f, z), new Vector3(12, 0.02f, 0.2f), markMat);
            Object.DestroyImmediate(m.GetComponent<Collider>());
        }

        // ---------- Rampa suave (15°) + plataforma ----------
        float h1 = Ramp(root, "Rampa 15", 20, 0, 6, 14, 15, rampMat);
        Platform(root, "Plataforma 15", 20, 14 * Mathf.Cos(15 * Mathf.Deg2Rad) - 0.1f, 6, h1, 10, platMat);

        // ---------- Rampa íngreme (30°) + plataforma ----------
        float h2 = Ramp(root, "Rampa 30", 32, 0, 6, 10, 30, rampMat);
        Platform(root, "Plataforma 30", 32, 10 * Mathf.Cos(30 * Mathf.Deg2Rad) - 0.1f, 6, h2, 10, platMat);

        // ---------- Rampa de lançamento (sem plataforma: pule dela!) ----------
        Ramp(root, "Rampa Lancamento", -20, 0, 6, 9, 22, rampMat);

        // ---------- Escada (testa o step offset) ----------
        for (int i = 0; i < 12; i++)
        {
            float h = 0.3f * (i + 1);
            Box(root, "Degrau " + i, new Vector3(-32, h / 2f, -10 + i * 0.8f + 0.4f), new Vector3(5, h, 0.8f), platMat);
        }

        // ---------- Túnel baixo (precisa do slide/agachar) ----------
        float tx = -8f;
        Box(root, "Tunel Teto", new Vector3(tx, 1.8f, 24), new Vector3(6, 1, 18), wallMat);       // face de baixo em y=1.3
        Box(root, "Tunel Parede E", new Vector3(tx + 3.5f, 1.5f, 24), new Vector3(1, 3, 18), wallMat);
        Box(root, "Tunel Parede D", new Vector3(tx - 3.5f, 1.5f, 24), new Vector3(1, 3, 18), wallMat);

        // ---------- Plataformas com vãos crescentes (teste de bhop / distância de pulo) ----------
        float[] gaps = { 2f, 3f, 4f, 5f, 6f, 7f };
        float pz = 12f;
        Box(root, "Gap 0", new Vector3(8, 0.5f, pz), new Vector3(4, 1, 4), platMat);
        for (int i = 0; i < gaps.Length; i++)
        {
            pz += 4f + gaps[i];
            Box(root, "Gap " + (i + 1), new Vector3(8, 0.5f, pz), new Vector3(4, 1, 4), platMat);
        }

        // ---------- Corredor com paredes (teste de colisão em alta velocidade) ----------
        Box(root, "Corredor E", new Vector3(4, 3, -35), new Vector3(1, 6, 40), wallMat);
        Box(root, "Corredor D", new Vector3(-4, 3, -35), new Vector3(1, 6, 40), wallMat);

        // ---------- Floresta de pilares (teste de controle no ar) ----------
        for (int x = -10; x <= 10; x += 5)
            for (int z = 45; z <= 65 - 5; z += 5)
                Box(root, "Pilar", new Vector3(x + ((z / 5) % 2 == 0 ? 0 : 2.5f), 2.5f, z), new Vector3(1, 5, 1), wallMat);

        // ---------- Luz (se a cena não tiver) ----------
        if (GameObject.Find("Directional Light") == null)
        {
            GameObject lg = new GameObject("Directional Light");
            Light l = lg.AddComponent<Light>();
            l.type = LightType.Directional;
            lg.transform.rotation = Quaternion.Euler(50, -30, 0);
        }

        CreatePlayer();

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log("[Strafe] Mapa de teste criado! Aperte Play. (Esc solta o mouse)");
    }

    // =====================================================================

    static void CreatePlayer()
    {
        GameObject player = new GameObject("Player");
        player.transform.position = new Vector3(0, 0.05f, -5);
        player.layer = 2; // Ignore Raycast

        CharacterController cc = player.AddComponent<CharacterController>();
        PlayerMovement pm = player.AddComponent<PlayerMovement>();
        cc.radius = pm.radius;
        cc.height = pm.standHeight;
        cc.center = new Vector3(0, pm.standHeight / 2f, 0);

        GameObject holder = new GameObject("CameraHolder");
        holder.transform.SetParent(player.transform, false);
        holder.transform.localPosition = new Vector3(0, pm.standHeight - pm.eyeOffset, 0);

        Camera cam = Camera.main;
        if (cam == null)
        {
            GameObject cg = new GameObject("Main Camera");
            cg.tag = "MainCamera";
            cam = cg.AddComponent<Camera>();
            cg.AddComponent<AudioListener>();
        }
        cam.transform.SetParent(holder.transform, false);
        cam.transform.localPosition = Vector3.zero;
        cam.transform.localRotation = Quaternion.identity;

        pm.cameraHolder = holder.transform;
        pm.playerCamera = cam;
        EditorUtility.SetDirty(pm);

        Selection.activeGameObject = player;
    }

    // Rampa que sobe na direção +Z, com a ponta baixa rente ao chão em z0. Retorna a altura do topo.
    static float Ramp(Transform parent, string name, float x, float z0, float width, float length, float angleDeg, Material mat)
    {
        float a = angleDeg * Mathf.Deg2Rad;
        float t = 0.5f;
        float y = length / 2f * Mathf.Sin(a) - t / 2f * Mathf.Cos(a);
        float z = z0 + t / 2f * Mathf.Sin(a) + length / 2f * Mathf.Cos(a);
        Box(parent, name, new Vector3(x, y, z), new Vector3(width, t, length), mat, new Vector3(-angleDeg, 0, 0));
        return length * Mathf.Sin(a);
    }

    static void Platform(Transform parent, string name, float x, float zStart, float width, float height, float depth, Material mat)
    {
        Box(parent, name, new Vector3(x, height / 2f, zStart + depth / 2f), new Vector3(width, height, depth), mat);
    }

    static GameObject Box(Transform parent, string name, Vector3 pos, Vector3 size, Material mat, Vector3 euler = default)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localRotation = Quaternion.Euler(euler);
        go.transform.localScale = size;
        go.GetComponent<Renderer>().sharedMaterial = mat;
        return go;
    }

    static Material MakeMat(string name, Color color)
    {
        const string folder = "Assets/Materials";
        if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets", "Materials");

        string path = folder + "/Strafe_" + name + ".mat";
        Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            Shader sh = Shader.Find("Universal Render Pipeline/Lit");
            if (sh == null) sh = Shader.Find("Standard");
            m = new Material(sh);
            AssetDatabase.CreateAsset(m, path);
        }
        m.color = color;
        EditorUtility.SetDirty(m);
        return m;
    }

    static void DestroyIfExists(string name)
    {
        GameObject go = GameObject.Find(name);
        if (go != null) Object.DestroyImmediate(go);
    }
}
