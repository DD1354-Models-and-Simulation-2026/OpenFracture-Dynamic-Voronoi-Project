using UnityEngine;

[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
[RequireComponent(typeof(Rigidbody))]
public class UnifiedFracture : MonoBehaviour
{
    public enum FractureAlgorithm
    {
        PlaneSlice,
        Voronoi
    }

    [Header("Algorithm")]
    public FractureAlgorithm algorithm = FractureAlgorithm.PlaneSlice;

    [Header("Trigger Options")]
    public TriggerOptions triggerOptions;

    [Header("Fracture Options")]
    public FractureOptions fractureOptions;

    [Header("Refracture Options")]
    public RefractureOptions refractureOptions;

    [Header("Callback Options")]
    public CallbackOptions callbackOptions;

    [HideInInspector]
    public int currentRefractureCount = 0;

    private GameObject fragmentRoot;

    public void CauseFracture()
    {
        callbackOptions.CallOnFracture(null, gameObject, transform.position);

        fractureOptions.impactPoint = Vector3.zero;
        fractureOptions.impactDirection = Vector3.up;

        ComputeFracture();
    }

    void OnCollisionEnter(Collision collision)
    {
        if (triggerOptions.triggerType != TriggerType.Collision)
            return;

        if (collision.contactCount == 0)
            return;

        var contact = collision.contacts[0];
        float collisionForce = collision.impulse.magnitude / Time.fixedDeltaTime;

        bool tagAllowed = triggerOptions.IsTagAllowed(contact.otherCollider.gameObject.tag);

        if (collisionForce > triggerOptions.minimumCollisionForce &&
            (triggerOptions.filterCollisionsByTag || tagAllowed))
        {
            callbackOptions.CallOnFracture(contact.otherCollider, gameObject, contact.point);

            // Set impact info (used by Voronoi, ignored by PlaneSlice)
            fractureOptions.impactPoint = transform.InverseTransformPoint(contact.point);
            fractureOptions.impactDirection =
                transform.InverseTransformDirection(collision.impulse.normalized);

            ComputeFracture();
        }
    }

    void OnTriggerEnter(Collider collider)
    {
        if (triggerOptions.triggerType != TriggerType.Trigger)
            return;

        bool tagAllowed = triggerOptions.IsTagAllowed(collider.gameObject.tag);

        if (triggerOptions.filterCollisionsByTag || tagAllowed)
        {
            callbackOptions.CallOnFracture(collider, gameObject, transform.position);

            fractureOptions.impactPoint = Vector3.zero;
            fractureOptions.impactDirection = Vector3.up;

            ComputeFracture();
        }
    }

    void Update()
    {
        if (triggerOptions.triggerType != TriggerType.Keyboard)
            return;

        if (Input.GetKeyDown(triggerOptions.triggerKey))
        {
            callbackOptions.CallOnFracture(null, gameObject, transform.position);

            fractureOptions.impactPoint = Vector3.zero;
            fractureOptions.impactDirection = Vector3.up;

            ComputeFracture();
        }
    }

    private void ComputeFracture()
    {
        Debug.Log("ComputeFracture called");
        var mesh = GetComponent<MeshFilter>().sharedMesh;
        if (mesh == null)
            return;

        if (fragmentRoot == null)
        {
            fragmentRoot = new GameObject($"{name}Fragments");
            fragmentRoot.transform.SetParent(transform.parent);
            fragmentRoot.transform.position = transform.position;
            fragmentRoot.transform.rotation = transform.rotation;
            fragmentRoot.transform.localScale = Vector3.one;
        }

        var fragmentTemplate = CreateFragmentTemplate();

        if (fractureOptions.asynchronous)
        {
            StartCoroutine(RunFractureAsync(fragmentTemplate));
        }
        else
        {
            RunFracture(fragmentTemplate);

            Destroy(fragmentTemplate);
            gameObject.SetActive(false);
            InvokeCompletionCallback();
        }
    }

    private System.Collections.IEnumerator RunFractureAsync(GameObject template)
    {
        if (algorithm == FractureAlgorithm.PlaneSlice)
        {
            yield return Fragmenter.FractureAsync(
                gameObject,
                fractureOptions,
                template,
                fragmentRoot.transform,
                null);
        }
        else
        {
            yield return VoronoiFragmenter.FractureAsync(
                gameObject,
                fractureOptions,
                template,
                fragmentRoot.transform,
                null);
        }

        Destroy(template);
        gameObject.SetActive(false);
        InvokeCompletionCallback();
    }

    private void RunFracture(GameObject template)
    {
        if (algorithm == FractureAlgorithm.PlaneSlice)
        {
            Fragmenter.Fracture(
                gameObject,
                fractureOptions,
                template,
                fragmentRoot.transform);
        }
        else
        {
            VoronoiFragmenter.Fracture(
                gameObject,
                fractureOptions,
                template,
                fragmentRoot.transform);
        }
    }

    private void InvokeCompletionCallback()
    {
        if ((currentRefractureCount == 0) ||
            (currentRefractureCount > 0 && refractureOptions.invokeCallbacks))
        {
            callbackOptions.onCompleted?.Invoke();
        }
    }

    private GameObject CreateFragmentTemplate()
    {
        GameObject obj = new GameObject("Fragment");
        obj.tag = tag;

        obj.AddComponent<MeshFilter>();

        var renderer = obj.AddComponent<MeshRenderer>();
        renderer.sharedMaterials = new Material[2]
        {
            GetComponent<MeshRenderer>().sharedMaterial,
            fractureOptions.insideMaterial
        };

        var thisCollider = GetComponent<Collider>();
        var fragmentCollider = obj.AddComponent<MeshCollider>();
        fragmentCollider.convex = true;
        fragmentCollider.sharedMaterial = thisCollider.sharedMaterial;
        fragmentCollider.isTrigger = thisCollider.isTrigger;

        var thisRb = GetComponent<Rigidbody>();
        var fragmentRb = obj.AddComponent<Rigidbody>();
        fragmentRb.linearVelocity = thisRb.linearVelocity;
        fragmentRb.angularVelocity = thisRb.angularVelocity;
        fragmentRb.linearDamping = thisRb.linearDamping;
        fragmentRb.angularDamping = thisRb.angularDamping;
        fragmentRb.useGravity = thisRb.useGravity;

        if (refractureOptions.enableRefracturing &&
            currentRefractureCount < refractureOptions.maxRefractureCount)
        {
            CopyFractureComponent(obj);
        }

        return obj;
    }

    private void CopyFractureComponent(GameObject obj)
    {
        var fractureComponent = obj.AddComponent<UnifiedFracture>();

        fractureComponent.algorithm = algorithm;
        fractureComponent.triggerOptions = triggerOptions;
        fractureComponent.fractureOptions = fractureOptions;
        fractureComponent.refractureOptions = refractureOptions;
        fractureComponent.callbackOptions = callbackOptions;
        fractureComponent.currentRefractureCount = currentRefractureCount + 1;
        fractureComponent.fragmentRoot = fragmentRoot;
    }
}