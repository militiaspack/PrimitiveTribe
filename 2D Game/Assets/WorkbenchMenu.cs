using UnityEngine;
using System.Collections;

public class WorkbenchMenu : MonoBehaviour {

	public GameObject WorkbenchUI;

	private bool interacting = false;

	void Start(){

		WorkbenchUI.SetActive(false);


	}


	void Update(){

		if (Input.GetButtonDown ("Interact")) {

			interacting = !interacting;

		}

		if (interacting) {

			WorkbenchUI.SetActive (true);
			Time.timeScale = 0;

		}

		if (!interacting) {

			WorkbenchUI.SetActive (false);
			Time.timeScale = 1;
		}
	}
}

