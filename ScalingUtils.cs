using UnityEngine;
using UnityEngine.Rendering;

namespace BiggerSprayMod;

public class ScalingUtils
{
    private static BiggerSprayMod _plugin;
    
    public ScalingUtils(BiggerSprayMod plugin)
    {
        _plugin = plugin;
    }
    
    public static void StartScalingMode()
    {
        _plugin._isScaling = true;
        _plugin._currentScalePreview = _plugin._configManager.SprayScale.Value;
        CreateScalePreview();
    }

    public static void StopScalingMode()
    {
        _plugin._isScaling = false;
        // Save the new scale value
        _plugin._configManager.SprayScale.Value = _plugin._currentScalePreview;

        // Clean up the preview object
        if (_plugin._scalePreviewObject != null)
        {
            Object.Destroy(_plugin._scalePreviewObject);
            _plugin._scalePreviewObject = null;
        }
    }
    
    public static void CreateScalePreview()
    {
        // Clean up any existing preview
        if (_plugin._scalePreviewObject != null)
        {
            Object.Destroy(_plugin._scalePreviewObject);
        }

        Ray ray = Camera.main.ViewportPointToRay(new Vector3(0.5f, 0.5f));
        if (Physics.Raycast(ray, out RaycastHit hitInfo, 100f))
        {
            // Create the preview quad
            _plugin._scalePreviewObject = GameObject.CreatePrimitive(PrimitiveType.Quad);
            _plugin._scalePreviewObject.name = "SprayScalePreview";

            // Remove the collider to avoid physics interactions
            Object.Destroy(_plugin._scalePreviewObject.GetComponent<Collider>());

            // Position and orient the preview
            Vector3 position = hitInfo.point + hitInfo.normal * 0.01f;
            Quaternion rotation = Quaternion.LookRotation(hitInfo.normal);
            _plugin._scalePreviewObject.transform.position = position;
            _plugin._scalePreviewObject.transform.rotation = rotation;

            // Apply the current scale to the preview
            UpdatePreviewScale(_plugin._currentScalePreview);

            // Create a material with the preview texture and color
            Material previewMaterial = new Material(_plugin._previewMaterialTemplate);
            if (_plugin._cachedSprayTexture != null)
            {
                previewMaterial.mainTexture = _plugin._cachedSprayTexture;
            }

            // Apply the preview color (semi-transparent)
            previewMaterial.color = _plugin._configManager.ScalePreviewColor.Value;

            // Apply shader settings for transparency
            previewMaterial.SetFloat("_Mode", 2); // Fade mode
            previewMaterial.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            previewMaterial.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            previewMaterial.SetInt("_ZWrite", 0);
            previewMaterial.DisableKeyword("_ALPHATEST_ON");
            previewMaterial.EnableKeyword("_ALPHABLEND_ON");
            previewMaterial.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            previewMaterial.renderQueue = 3000;

            // Assign the material to the preview
            _plugin._scalePreviewObject.GetComponent<MeshRenderer>().material = previewMaterial;
        }
    }
    
    public static void UpdatePreviewScale(float scale)
    {
        if (_plugin._scalePreviewObject == null) return;

        // Calculate an aspect ratio to maintain proportions
        Vector2 adjustedScale = _plugin._scalingUtils.CalculateContainedScale(scale);
        _plugin._scalePreviewObject.transform.localScale = new Vector3(adjustedScale.x, adjustedScale.y, 1f);
    }

    public void UpdateScalingPreview()
    {
        if (_plugin._scalePreviewObject == null)
        {
            CreateScalePreview();
            return;
        }

        // Check if we need to update the position
        Ray ray = Camera.main.ViewportPointToRay(new Vector3(0.5f, 0.5f));
        if (Physics.Raycast(ray, out RaycastHit hitInfo, 100f))
        {
            Vector3 position = hitInfo.point + hitInfo.normal * 0.01f;
            Quaternion rotation = Quaternion.LookRotation(hitInfo.normal);
            _plugin._scalePreviewObject.transform.position = position;
            _plugin._scalePreviewObject.transform.rotation = rotation;
        }
    }

    public Vector2 CalculateContainedScale(float baseScale)
    {
        // Default to square if no texture
        if (_plugin._cachedSprayTexture == null)
        {
            return new Vector2(baseScale, baseScale);
        }

        // Get dimensions and calculate aspect ratio
        float width = _plugin._originalImageDimensions.x;
        float height = _plugin._originalImageDimensions.y;
        float aspectRatio = width / height;

        // Logic to contain the image within a square of size baseScale
        if (width > height)
        {
            // Wider than tall
            return new Vector2(baseScale, baseScale / aspectRatio);
        }
        else
        {
            // Taller than wide or square
            return new Vector2(baseScale * aspectRatio, baseScale);
        }
    }
    
    public void AdjustScale(float amount)
    {
        // If in preview mode, adjust the preview
        if (_plugin._isScaling)
        {
            _plugin._currentScalePreview = Mathf.Clamp(
                _plugin._currentScalePreview + amount,
                _plugin._configManager.MinScaleSize.Value,
                _plugin._configManager.MaxScaleSize.Value
            );
            
            // Update the preview if it exists
            if (_plugin._scalePreviewObject != null)
            {
                UpdatePreviewScale(_plugin._currentScalePreview);
            }
        }
        // Otherwise update the config value directly
        else
        {
            _plugin._configManager.SprayScale.Value = Mathf.Clamp(
                _plugin._configManager.SprayScale.Value + amount,
                _plugin._configManager.MinScaleSize.Value,
                _plugin._configManager.MaxScaleSize.Value
            );
        }
    }
}