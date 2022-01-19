from locust import HttpUser, task
import uuid

class PlayerServiceUser(HttpUser):
# 	wait_space = between(1, 5)

	@task(1)
	def hello_world(self):
		self.client.get("/player/v2/health", name="/health")
		
# 	@task(1)
# 	def launch(self):
# 		self.client.post("/player/v2/launch", json={"installId": "locust-" + uuid.uuid4().hex}, name="/launch")
