using UnityEngine;
using UnityEngine.UI;
using System.Collections;

public class menuscript : MonoBehaviour {

	public Canvas quitMenu;
	public Button startText;
	public Button exitText;


	void Start () 
	
		{
			startText = startText.GetComponent<Button> ();
			exitText = exitText.GetComponent<Button> ();
	
		}

		public void ExitPress()

		{

		Application.Quit ();
	   }

		public void NoPress()
	{

		startText.enabled = true;
		exitText.enabled = true;

	}

			public void StartLevel()

			{
				Application.LoadLevel (1);
			}
		
	
	// Update is called once per frame
	void Update () {
	
	}
}
