using System.Collections.Generic;
using UnityEngine;
using VisualPinball.Engine.VPT;
using Material = UnityEngine.Material;
using Mesh = UnityEngine.Mesh;
using Texture = VisualPinball.Engine.VPT.Texture;

namespace VisualPinball.Unity.Importer.AssetHandler
{
	public interface IAssetHandler
	{
		void HandleTextureData(Texture texture);
		void ImportTextures(IEnumerable<Texture> textures);
		Texture2D LoadTexture(Texture texture);
		void SaveMaterial(PbrMaterial material, Material unityMaterial);
		void OnMaterialsSaved(PbrMaterial[] materials);
		Material LoadMaterial(PbrMaterial material);
		void OnMeshesImported(GameObject gameObject);
		void SaveMesh(Mesh mesh);
	}
}